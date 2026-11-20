// Integrado.cs - Magpie (y mpv y el motor) como piezas de la propia app, sin depender de nada instalado aparte (18-11-2026, agente
// INTEGRADO; el usuario: "que no haya que descargarse magpie, que sea como propia").
//  - Paths.AppDir: la carpeta del exe si es una carpeta completa de la app (la que monta scripts\empaquetar.ps1); antes, en la app original, una carpeta fija
//    (asi los exe de prueba compilados en otra carpeta siguen usando la app de siempre)
//  - magpie\ con ficheros propios de verdad. Antes eran enlaces duros al Magpie instalado aparte: el mismo fichero en los dos sitios
//    (una actualizacion del Magpie del usuario escrita encima cambiaba tambien el de la app, y con uno abierto el otro no se podia
//    cambiar). Se separan una vez (se copian). magpie_origen ya no hace falta: si esta y tiene ficheros mas nuevos, como mucho se
//    actualiza desde el (magpie_actualizar); si no esta, la app sigue con lo suyo y nunca pide instalar ni descargar Magpie
//  - Sin Updater.exe en la copia (el fork tiene la busqueda de actualizaciones quitada al compilar: no lo usa nunca)
//  - Sin el icono de Magpie en la bandeja (MagpieOculto): Magpie tiene que llevarlo para arrancar sin su ventana (-t), asi que se le
//    quita desde fuera en cuanto lo pone (y si el explorador se reinicia y lo vuelve a poner)
//  - mpv\mpv.exe y motor\Switch2Engine.exe dentro de la app si existen (usar_carpetas_app=1), con las rutas del ini de respaldo
//  - Sin nvngx_dlssnr.dll (un paquete para repartir no la lleva: es una DLL de NVIDIA modificada, sin firma): DLSS 5 no disponible y la
//    app va sin el (con motor=propio, el motor propio), en vez de pedirle a Magpie un filtro que no puede cargar
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ConsoleEnhancer
{
    static class Integrado
    {
        // ConsoleEnhancer (fase 2, UNIVERSAL): sin rutas de ningun PC. La app es la carpeta del exe; mpv y el motor van dentro
        public const string DefaultMpv = "";
        public const string DefaultEngine = "";
        public const string Dlss5Dll = "nvngx_dlssnr.dll";
        public const string Dlss5MissingText = "DLSS 5 no disponible";

        // La carpeta de la app: SIEMPRE la del exe (portable: donde se descomprima). NO usa Paths (lo inicializa)
        public static string FindAppDir()
        {
            try
            {
                Assembly a = Assembly.GetEntryAssembly();
                if (a != null && !string.IsNullOrEmpty(a.Location)) return Path.GetDirectoryName(Path.GetFullPath(a.Location)).TrimEnd('\\');
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        }

        // Los datos (ini, log, juegos\, calibracion): <app>\datos si esta portable.txt y se puede escribir ahi; si no,
        // %LOCALAPPDATA%\ConsoleEnhancer. Pruebas: --datos=<carpeta> (lo mira Program.Main antes de tocar Paths)
        public static string FindDataDir(string appDir)
        {
            string d = null;
            try
            {
                foreach (string a in Environment.GetCommandLineArgs())
                    if (a.StartsWith("--datos=", StringComparison.OrdinalIgnoreCase)) d = Path.GetFullPath(a.Substring(8));
                if (d == null && File.Exists(Path.Combine(appDir, "portable.txt")))
                {
                    string p = Path.Combine(appDir, "datos");
                    Directory.CreateDirectory(p);
                    string probe = Path.Combine(p, ".escribir");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    d = p;
                }
            }
            catch { d = null; }
            if (d == null) d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConsoleEnhancer");
            try { Directory.CreateDirectory(Path.Combine(d, "juegos")); Directory.CreateDirectory(Path.Combine(d, "logs")); } catch { }
            return d.TrimEnd('\\');
        }

        // ---- Rutas de mpv y del motor (al leer el ini: Config.Load) ----
        public static string AppMpv { get { return Path.Combine(Paths.AppDir, @"mpv\mpv.exe"); } }
        public static string AppEngine { get { return Path.Combine(Paths.AppDir, @"motor\ConsoleEnhancerEngine.exe"); } }

        // magpie_actualizar (Config.MagpieUpdate): 0 nunca mira el origen, 1 coge del origen solo lo que sea mas nuevo, 2 lo deja igual que el origen
        public static volatile int MagpieUpdate = 1;
        static int pathsLogged;

        public static void ResolvePaths(Config c)
        {
            MagpieUpdate = c.MagpieUpdate;
            string mpvIni = c.MpvPath, engIni = c.EnginePath;
            c.MpvPath = Pick(c.AppFolders, AppMpv, c.MpvPath, DefaultMpv);
            c.EnginePath = Pick(c.AppFolders, AppEngine, c.EnginePath, DefaultEngine);
            if (Interlocked.Exchange(ref pathsLogged, 1) == 0)
                Log.Write("Rutas: app " + Paths.AppDir + ", Magpie " + Paths.MagpieDir + ", mpv " + c.MpvPath +
                          (string.Equals(c.MpvPath, mpvIni, StringComparison.OrdinalIgnoreCase) ? "" : " (ini: " + mpvIni + ")") +
                          ", motor " + c.EnginePath + (string.Equals(c.EnginePath, engIni, StringComparison.OrdinalIgnoreCase) ? "" : " (ini: " + engIni + ")"));
        }

        // El de dentro de la app si existe (y se quiere); si no, el del ini; si ese no existe, el de dentro o el de siempre
        static string Pick(bool inside, string own, string ini, string def)
        {
            try
            {
                if (inside && File.Exists(own)) return own;
                if (!string.IsNullOrEmpty(ini) && File.Exists(ini)) return ini;
                if (File.Exists(own)) return own;
                if (File.Exists(def)) return def;
            }
            catch { }
            return string.IsNullOrEmpty(ini) ? def : ini;
        }

        public static string Describe(Config c)
        {
            return "app " + Paths.AppDir + ", Magpie " + Paths.MagpieDir + " (" + MagpieVersion(Paths.MagpieDir) + (Dlss5Available ? "" : ", sin " + Dlss5Dll + ": " + Dlss5MissingText) +
                   "), origen " + (string.IsNullOrEmpty(c.MagpieSource) ? "ninguno" : c.MagpieSource + (File.Exists(Path.Combine(c.MagpieSource, "Magpie.exe")) ? "" : " (no esta)")) +
                   ", actualizar " + (MagpieUpdate <= 0 ? "nunca" : MagpieUpdate == 1 ? "lo mas nuevo" : "igual que el origen") +
                   ", mpv " + c.MpvPath + ", motor " + c.EnginePath;
        }

        // ---- DLSS 5 (el filtro DLSSNR de Magpie) disponible: esta su DLL ----
        static volatile int dlss5State; // 0 sin mirar, 1 si, -1 no
        public static bool Dlss5Available
        {
            get
            {
                if (dlss5State == 0) dlss5State = File.Exists(Path.Combine(Paths.MagpieDir, Dlss5Dll)) ? 1 : -1;
                return dlss5State > 0;
            }
        }
        // (20-11, PUBLICAR) el usuario ha puesto o quitado la DLL con el boton (Extras.cs): mirarlo otra vez
        public static void Dlss5Rever() { dlss5State = 0; }

        // ---- La copia de Magpie de la app ----
        // Lo que nunca viene del origen: la config, los logs y la cache de efectos son de la app; Updater.exe no hace falta; los logs de
        // NGX junto al exe; restos de una copia a medias
        static bool Skip(string rel)
        {
            string top = rel.Split('\\')[0].ToLowerInvariant();
            if (top == "config" || top == "logs" || top == "cache" || top == "update") return true;
            if (rel.IndexOf('\\') < 0 && (rel.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || rel.Equals("Updater.exe", StringComparison.OrdinalIgnoreCase) ||
                                         rel.Equals("update.zip", StringComparison.OrdinalIgnoreCase))) return true;
            return rel.EndsWith(TmpExt, StringComparison.OrdinalIgnoreCase);
        }

        // Los que un paquete para repartir no lleva (scripts\empaquetar.ps1): no cuentan como "falta" en el manifiesto. Sin firma de NVIDIA:
        // nvngx_dlssnr.dll (modificada, sin firma) y runtimes\dlssg_sm86 (de la comunidad: Magpie lo copia a %LOCALAPPDATA%\Magpie\DlssgSm86)
        static bool Optional(string rel)
        {
            return rel.Equals(Dlss5Dll, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(@"runtimes\dlssg_sm86", StringComparison.OrdinalIgnoreCase) ||
                   // (ConsoleEnhancer: las DLL de NVIDIA VFX, ~480 MB, van aparte: empaquetar.ps1 -IncluirVfx)
                   Regex.IsMatch(rel, @"^(nvVFX.*\.dll|NVVideoEffects\.dll|NVCVImage\.dll|npp.*64_12\.dll|cudart64_12\.dll|nvidia-vfx-runtime-manifest\.json)$", RegexOptions.IgnoreCase) ||
                   rel.StartsWith(@"NVIDIA-VFX-Licenses", StringComparison.OrdinalIgnoreCase);
        }

        const string TmpExt = ".s2nuevo";

        // (Pack = 4: los FILETIME de Windows van alineados a 4; con 8 se leia el tamaño como numero de enlaces)
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct BY_HANDLE_FILE_INFORMATION
        {
            public uint attrs;
            public long created, accessed, written;
            public uint volume, sizeHigh, sizeLow, links, indexHigh, indexLow;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, out BY_HANDLE_FILE_INFORMATION info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

        // Cuantos nombres (enlaces duros) tiene el fichero. Abierto solo para leer sus atributos (sin leer datos: el antivirus no lo
        // analiza al abrirlo; con lectura normal, la primera vez tras copiarlos eran ~23 s para los ~300)
        static int Links(string path)
        {
            try
            {
                using (Microsoft.Win32.SafeHandles.SafeFileHandle h = CreateFileW(path, 0x80 /* FILE_READ_ATTRIBUTES */, 7, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return 1;
                    BY_HANDLE_FILE_INFORMATION i;
                    return GetFileInformationByHandle(h, out i) ? (int)i.links : 1;
                }
            }
            catch { return 1; }
        }

        // Pone 'from' en 'dst' como fichero propio (copia; con la misma fecha). Por un temporal al lado: si se corta, la vez siguiente
        // se termina (Prepare, al empezar)
        static void CopyInto(string from, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            string tmp = dst + TmpExt;
            DateTime when = File.GetLastWriteTimeUtc(from);
            File.Copy(from, tmp, true);
            File.SetLastWriteTimeUtc(tmp, when);
            if (File.Exists(dst)) File.Delete(dst); // (un enlace: solo se quita este nombre, el original sigue igual)
            File.Move(tmp, dst);
        }

        static void CloseForChange(string why)
        {
            if (MagpieLive.Running) MagpieLive.Close(why); // (con el abierto no se pueden cambiar sus ficheros)
        }

        public static string MagpieVersion(string dir)
        {
            try
            {
                string m = Path.Combine(dir, "build-manifest.json");
                if (File.Exists(m))
                {
                    Match v = Regex.Match(File.ReadAllText(m, Encoding.UTF8), "\"version\"\\s*:\\s*\"([^\"]+)\"");
                    if (v.Success) return "v" + v.Groups[1].Value;
                }
                string exe = Path.Combine(dir, "Magpie.exe");
                if (File.Exists(exe)) return "v" + FileVersionInfo.GetVersionInfo(exe).FileVersion;
            }
            catch { }
            return "version ?";
        }

        // Deja lista la copia de la app: ficheros propios, actualizada desde el origen si toca y comprobada con su build-manifest.json.
        // true si hay Magpie.exe (lo que no se haya podido arreglar queda en el log). Con Magpie abierto lo cierra solo si hay que cambiar algo
        public static bool PrepareMagpie(string source, out string detail)
        {
            string dir = Paths.MagpieDir;
            List<string> notes = new List<string>();
            Directory.CreateDirectory(dir);
            int failed = 0;

            // 0. Restos de una vez anterior cortada a medias
            try
            {
                foreach (string t in Directory.GetFiles(dir, "*" + TmpExt, SearchOption.AllDirectories))
                {
                    string f = t.Substring(0, t.Length - TmpExt.Length);
                    try { if (!File.Exists(f)) File.Move(t, f); else File.Delete(t); } catch { }
                }
            }
            catch { }

            // 1. Ficheros propios: los que aun son enlaces duros (al Magpie del usuario) se copian. Una vez (~1 GB)
            List<string> linked = new List<string>();
            long linkedBytes = 0;
            Stopwatch scan = Stopwatch.StartNew();
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string top = f.Substring(dir.Length + 1).Split('\\')[0].ToLowerInvariant();
                if (top == "config" || top == "logs") continue;
                if (Links(f) > 1) { linked.Add(f); linkedBytes += new FileInfo(f).Length; }
            }
            if (scan.ElapsedMilliseconds > 300) notes.Add("mirar los enlaces: " + scan.ElapsedMilliseconds + " ms");
            if (linked.Count > 0)
            {
                Stopwatch sw = Stopwatch.StartNew();
                CloseForChange("separar sus ficheros de los del Magpie instalado");
                int ok = 0;
                foreach (string f in linked)
                {
                    try { CopyInto(f, f); ok++; }
                    catch (Exception ex) { if (++failed <= 3) Log.Write("Aviso: Magpie de la app, separando " + f.Substring(dir.Length + 1) + ": " + ex.Message); }
                }
                notes.Add(ok + " de " + linked.Count + " ficheros que eran enlaces duros ya son propios (" + (linkedBytes >> 20) + " MB, " + sw.ElapsedMilliseconds + " ms)");
            }

            // 2. Updater.exe fuera: el fork no busca actualizaciones (quitado al compilar) y la app no lo quiere
            try
            {
                string up = Path.Combine(dir, "Updater.exe");
                if (File.Exists(up)) { CloseForChange("quitar su Updater.exe"); File.Delete(up); notes.Add("quitado Updater.exe"); }
            }
            catch (Exception ex) { notes.Add("Updater.exe sin quitar (" + ex.Message + ")"); }

            // 3. Actualizar desde el origen (opcional)
            string srcRoot = null;
            try
            {
                if (MagpieUpdate <= 0) notes.Add("origen: no se mira (magpie_actualizar=0)");
                else if (string.IsNullOrEmpty(source)) notes.Add("sin origen (no hace falta)");
                else if (!File.Exists(Path.Combine(source, "Magpie.exe"))) notes.Add("origen " + source + " no esta (no hace falta: va con la suya)");
                else if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) notes.Add("origen = la propia copia");
                else
                {
                    srcRoot = Path.GetFullPath(source).TrimEnd('\\');
                    string upd = Update(srcRoot, dir, ref failed);
                    if (upd != null) notes.Add(upd);
                }
            }
            catch (Exception ex) { failed++; notes.Add("actualizacion fallida, sigue la copia de antes (" + ex.Message + ")"); }

            // 4. Comprobada con su manifiesto (lo que el paquete de Magpie dice que lleva): lo que falte se coge del origen si se puede
            try { string chk = CheckManifest(dir, srcRoot, ref failed); if (chk != null) notes.Add(chk); }
            catch (Exception ex) { notes.Add("manifiesto sin comprobar (" + ex.Message + ")"); }

            Directory.CreateDirectory(Path.Combine(dir, "logs"));
            Directory.CreateDirectory(Path.Combine(dir, @"config\v4e"));
            dlss5State = 0;
            if (!Dlss5Available) notes.Add("sin " + Dlss5Dll + ": " + Dlss5MissingText + " (la app va sin el)");
            // (el fork hace SIEMPRE el DLSS FG de Magpie por este runtime de la comunidad, tambien en RTX 40: sin el "No native fallback")
            if (!File.Exists(Path.Combine(dir, @"runtimes\dlssg_sm86\version.dll"))) notes.Add(@"sin runtimes\dlssg_sm86: la generacion DLSS FG de Magpie no va (Smooth Motion y la del motor propio si)");
            bool exists = File.Exists(Path.Combine(dir, "Magpie.exe"));
            detail = MagpieVersion(dir) + (exists ? "" : ", NO ESTA Magpie.exe") + (notes.Count > 0 ? "; " + string.Join("; ", notes.ToArray()) : "; al dia") +
                     (failed > 0 ? " (" + failed + " fallos)" : "");
            return exists;
        }

        // Los ficheros del origen que tocan (mas nuevos, o distintos con magpie_actualizar=2) se copian primero aparte (magpie.nuevo junto
        // a magpie) y solo si estan todos se ponen en su sitio: un fallo a medias no deja una copia mezclada
        static string Update(string srcRoot, string dir, ref int failed)
        {
            List<string> rels = new List<string>();
            long bytes = 0;
            foreach (string f in Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(srcRoot.Length + 1);
                if (Skip(rel)) continue;
                FileInfo s = new FileInfo(f), d = new FileInfo(Path.Combine(dir, rel));
                bool take = !d.Exists || (MagpieUpdate >= 2 ? s.Length != d.Length || s.LastWriteTimeUtc != d.LastWriteTimeUtc : s.LastWriteTimeUtc > d.LastWriteTimeUtc);
                if (take) { rels.Add(rel); bytes += s.Length; }
            }
            if (rels.Count == 0) return "origen " + srcRoot + " (" + MagpieVersion(srcRoot) + "): nada mas nuevo";
            Stopwatch sw = Stopwatch.StartNew();
            string stage = dir.TrimEnd('\\') + ".nuevo";
            try
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                foreach (string rel in rels)
                {
                    string to = Path.Combine(stage, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(Path.Combine(srcRoot, rel), to, true);
                    File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(Path.Combine(srcRoot, rel)));
                }
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
                failed++;
                return "actualizacion desde " + srcRoot + " fallida al copiar, sigue la copia de antes (" + ex.Message + ")";
            }
            string before = MagpieVersion(dir);
            CloseForChange("actualizar su copia desde " + srcRoot);
            int ok = 0;
            foreach (string rel in rels)
            {
                try
                {
                    string dst = Path.Combine(dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(Path.Combine(stage, rel), dst);
                    ok++;
                }
                catch (Exception ex) { if (++failed <= 3) Log.Write("Aviso: Magpie de la app, actualizando " + rel + ": " + ex.Message); }
            }
            try { Directory.Delete(stage, true); } catch { }
            return "actualizado desde " + srcRoot + ": " + ok + " de " + rels.Count + " ficheros (" + (bytes >> 20) + " MB, " + sw.ElapsedMilliseconds + " ms), " + before + " -> " + MagpieVersion(dir);
        }

        static string CheckManifest(string dir, string srcRoot, ref int failed)
        {
            string mf = Path.Combine(dir, "build-manifest.json");
            if (!File.Exists(mf)) return "sin build-manifest.json (no se comprueba)";
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> root = js.DeserializeObject(File.ReadAllText(mf, Encoding.UTF8)) as Dictionary<string, object>;
            object fo;
            if (root == null || !root.TryGetValue("files", out fo) || !(fo is object[])) return "build-manifest.json sin lista de ficheros";
            List<string> missing = new List<string>(), changed = new List<string>(), optionalOut = new List<string>();
            int total = 0;
            foreach (object o in (object[])fo)
            {
                Dictionary<string, object> e = o as Dictionary<string, object>;
                if (e == null || !e.ContainsKey("path")) continue;
                string rel = ((string)e["path"]).Replace('/', '\\');
                if (Skip(rel)) continue; // (Updater.exe: fuera a proposito)
                long want = e.ContainsKey("bytes") && e["bytes"] != null ? Convert.ToInt64(e["bytes"]) : -1;
                total++;
                FileInfo d = new FileInfo(Path.Combine(dir, rel));
                if (!d.Exists)
                {
                    // Del origen si lo tiene igual que el manifiesto
                    FileInfo s = srcRoot != null ? new FileInfo(Path.Combine(srcRoot, rel)) : null;
                    if (s != null && s.Exists && (want < 0 || s.Length == want) && !Optional(rel))
                    {
                        try { CloseForChange("reponer ficheros que le faltan"); CopyInto(s.FullName, d.FullName); continue; }
                        catch (Exception ex) { failed++; Log.Write("Aviso: Magpie de la app, reponiendo " + rel + ": " + ex.Message); }
                    }
                    if (Optional(rel)) optionalOut.Add(rel); else missing.Add(rel);
                }
                else if (want >= 0 && d.Length != want) changed.Add(rel);
            }
            StringBuilder sb = new StringBuilder("manifiesto: " + total + " ficheros");
            if (missing.Count == 0 && changed.Count == 0) sb.Append(" bien");
            if (missing.Count > 0) sb.Append(", FALTAN ").Append(missing.Count).Append(" (").Append(string.Join(", ", missing.GetRange(0, Math.Min(5, missing.Count)).ToArray())).Append(missing.Count > 5 ? "..." : "").Append(")");
            if (changed.Count > 0) sb.Append(", ").Append(changed.Count).Append(" de otro tamaño (cambiados a mano: ").Append(string.Join(", ", changed.GetRange(0, Math.Min(5, changed.Count)).ToArray())).Append(")");
            if (optionalOut.Count > 0) sb.Append(", sin los opcionales (").Append(optionalOut.Count).Append(")");
            return sb.ToString();
        }
    }

    // ===================================================================
    //  El icono de Magpie en la bandeja, fuera. Magpie solo arranca sin su ventana principal (-t) si lleva el icono (showNotifyIcon: sin
    //  el ignora -t y abre su ventana), asi que se le quita desde fuera: Shell_NotifyIcon(NIM_DELETE) con su ventana Magpie_NotifyIcon y
    //  su id (0, sin GUID: NotifyIconService.cpp del fork). Magpie no se entera (cree que lo tiene) y no lo vuelve a poner salvo si el
    //  explorador se reinicia (TaskbarCreated): por eso un repaso. Tampoco quedan iconos fantasma al matarlo (la app lo mata siempre)
    // ===================================================================
    static class MagpieOculto
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID, uFlags, uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct NOTIFYICONIDENTIFIER { public int cbSize; public IntPtr hWnd; public int uID; public Guid guidItem; }
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int l, t, r, b; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(int msg, ref NOTIFYICONDATAW d);
        [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        public const string IconClass = "Magpie_NotifyIcon";
        const int NIM_DELETE = 2;
        static int watching, logged;
        static volatile int startedTick = Environment.TickCount - 600000;
        static readonly HashSet<IntPtr> deleted = new HashSet<IntPtr>();
        static int lastBlind = Environment.TickCount;

        // ¿Esta puesto el icono de esa ventana? (1 si, 0 no, -1 no se sabe)
        public static int Present(IntPtr h)
        {
            try
            {
                NOTIFYICONIDENTIFIER id = new NOTIFYICONIDENTIFIER();
                id.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
                id.hWnd = h;
                RECT r;
                int hr = Shell_NotifyIconGetRect(ref id, out r);
                return hr == 0 ? 1 : 0;
            }
            catch { return -1; }
        }

        static bool Delete(IntPtr h)
        {
            NOTIFYICONDATAW d = new NOTIFYICONDATAW();
            d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATAW));
            d.hWnd = h;
            d.uID = 0;
            return Shell_NotifyIconW(NIM_DELETE, ref d);
        }

        // Las ventanas del icono de un Magpie.exe que hay ahora
        public static List<IntPtr> IconWindows()
        {
            List<IntPtr> l = new List<IntPtr>();
            foreach (IntPtr parent in new IntPtr[] { IntPtr.Zero, new IntPtr(-3) }) // (normales y "solo mensajes", HWND_MESSAGE)
                for (IntPtr h = FindWindowEx(parent, IntPtr.Zero, IconClass, null); h != IntPtr.Zero; h = FindWindowEx(parent, h, IconClass, null))
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (MagpieLive.IsMagpiePid(pid, true) && !l.Contains(h)) l.Add(h);
                }
            return l;
        }

        // Quita ya el icono de los Magpie abiertos (la primera vez que se ve cada ventana y siempre que se vea puesto). Devuelve cuantos
        public static int Hide(string how)
        {
            int n = 0;
            try
            {
                bool blind = unchecked(Environment.TickCount - lastBlind) > 10000; // (cada 10 s, por si no se pudiera mirar si esta)
                if (blind) lastBlind = Environment.TickCount;
                foreach (IntPtr h in IconWindows())
                {
                    bool fresh;
                    lock (deleted) { fresh = !deleted.Contains(h); if (deleted.Count > 64) deleted.Clear(); }
                    int present = Present(h);
                    if (!fresh && present == 0 && !blind) continue;
                    bool ok = Delete(h);
                    lock (deleted) deleted.Add(h);
                    if (ok)
                    {
                        n++;
                        if (Interlocked.Increment(ref logged) <= 6)
                            Log.Write("Icono de Magpie en la bandeja quitado (" + how + (present == 1 ? "" : present == 0 ? ", no se veia puesto" : "") + ")");
                    }
                }
            }
            catch (Exception ex) { if (Interlocked.Increment(ref logged) <= 6) Log.Error("Quitando el icono de Magpie de la bandeja", ex); }
            return n;
        }

        // Al abrir Magpie (MagpieLive.Start): repaso cada 50 ms los primeros 15 s (pone el icono justo despues de crear su ventana) y luego
        // cada 2 s (el explorador reiniciado se lo vuelve a pedir). Un hilo para toda la app
        public static void Watch()
        {
            startedTick = Environment.TickCount;
            if (Interlocked.Exchange(ref watching, 1) != 0) return;
            Thread t = new Thread(delegate ()
            {
                while (true)
                {
                    bool early = unchecked(Environment.TickCount - startedTick) < 15000;
                    try { Hide(early ? "al abrirse" : "repaso"); } catch { }
                    Thread.Sleep(early ? 50 : 2000);
                }
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }
    }
}
