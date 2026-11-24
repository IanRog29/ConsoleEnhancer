// SteamAcceso.cs - 24-11-2026 (1.3.3, VARIOS): "Añadir a Steam" en el panel de ajustes (ConsoleEnhancer Settings.exe, src\Lanzador\Panel.cs).
//
// El usuario: "Si es legal añade en los ajustes generales de la app (los del archivo) una opcion de añadir a steam para que aparezca en la
// app." Es lo mismo que hace Steam con "Añadir un juego ajeno a Steam": una entrada en el shortcuts.vdf del usuario de Steam
// (<Steam>\userdata\<id de cuenta>\config\shortcuts.vdf, formato KeyValues binario). Se escribe solo:
//  - con una lectura completa del archivo que entiende TODO lo que trae (si hay algo raro o esta cortado, no se toca y se dice);
//  - sin duplicar: si ya hay una entrada de ConsoleEnhancer (por el nombre del exe o el de la app) se actualiza esa (la ruta de una version
//    nueva en otra carpeta), con su appid de siempre (sus horas y colecciones se quedan);
//  - con copia de seguridad: shortcuts.vdf.antes-ce (la primera vez, la de antes de ConsoleEnhancer) y shortcuts.vdf.ce-copia (la ultima);
//    se escribe en un archivo aparte, se vuelve a leer para comprobarlo y solo entonces sustituye al de verdad;
//  - con el icono de la app (un .ico en la carpeta de los ajustes, que sobrevive a las versiones nuevas) y sin el overlay de Steam (no se
//    mete en el motor);
//  - Steam lee el archivo al abrirse: si esta abierto, el panel ofrece cerrarlo y volverlo a abrir (y si no, avisa de que hay que hacerlo).
// "Quitar de Steam" borra solo las entradas de ConsoleEnhancer (las demas, byte a byte como estaban). Clave del ini steam_acceso (0 de serie):
// 1 = puesto desde el panel; con ella a 1, al abrir la app con Steam cerrado se corrige la ruta si apunta a otra carpeta (version nueva).
// Pruebas: CE_STEAM_DIR=<carpeta falsa con userdata\...> (nunca cierra ni abre Steam) y --test-steam. C# 5
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ConsoleEnhancer
{
    static class SteamAcceso
    {
        public const string Nombre = "ConsoleEnhancer";
        const string ExeNombre = "ConsoleEnhancer.exe";
        public static string RaizPrueba;   // (pruebas: la carpeta de Steam falsa; tambien CE_STEAM_DIR)

        // ------------------------------------------------------------------ KeyValues binario
        sealed class Nodo
        {
            public byte Tipo;          // 0 mapa, 1 texto, 2 int32, 3 float, 4 puntero, 5 texto ancho, 6 color, 7 uint64, 10 int64
            public string Clave;
            public byte[] Valor;       // texto (UTF-8 sin el 0 final) o los bytes fijos; null en un mapa
            public List<Nodo> Hijos;   // mapa
            public string Texto { get { return Valor == null ? "" : Encoding.UTF8.GetString(Valor); } }
            public Nodo Hijo(string k)
            {
                if (Hijos == null) return null;
                foreach (Nodo n in Hijos) if (string.Equals(n.Clave, k, StringComparison.OrdinalIgnoreCase)) return n;
                return null;
            }
        }

        sealed class Lector
        {
            readonly byte[] b; int p;
            public Lector(byte[] datos) { b = datos; }
            byte[] HastaCero()
            {
                int i = p;
                while (i < b.Length && b[i] != 0) i++;
                if (i >= b.Length) throw new InvalidDataException("texto sin terminar en " + p);
                byte[] r = new byte[i - p];
                Buffer.BlockCopy(b, p, r, 0, r.Length);
                p = i + 1;
                return r;
            }
            byte[] Fijos(int n)
            {
                if (p + n > b.Length) throw new InvalidDataException("cortado en " + p);
                byte[] r = new byte[n];
                Buffer.BlockCopy(b, p, r, 0, n);
                p += n;
                return r;
            }
            // Hijos de un mapa hasta su 8 (arriba del todo, hasta el 8 final o el final del archivo)
            public List<Nodo> Mapa(bool raiz)
            {
                List<Nodo> l = new List<Nodo>();
                while (true)
                {
                    if (p >= b.Length) { if (raiz) return l; throw new InvalidDataException("mapa sin cerrar"); }
                    byte t = b[p++];
                    if (t == 8 || t == 11)
                    {
                        if (raiz && p != b.Length) throw new InvalidDataException("datos despues del final en " + p);
                        return l;
                    }
                    Nodo n = new Nodo { Tipo = t, Clave = Encoding.UTF8.GetString(HastaCero()) };
                    switch (t)
                    {
                        case 0: n.Hijos = Mapa(false); break;
                        case 1: n.Valor = HastaCero(); break;
                        case 2: case 3: case 4: case 6: n.Valor = Fijos(4); break;
                        case 7: case 10: n.Valor = Fijos(8); break;
                        case 5:
                            {
                                int i = p;
                                while (i + 1 < b.Length && (b[i] != 0 || b[i + 1] != 0)) i += 2;
                                if (i + 1 >= b.Length) throw new InvalidDataException("texto ancho sin terminar");
                                n.Valor = Fijos(i - p); p += 2;
                                break;
                            }
                        default: throw new InvalidDataException("tipo desconocido " + t + " en " + (p - 1));
                    }
                    l.Add(n);
                }
            }
        }

        static void Escribe(Stream s, List<Nodo> l)
        {
            foreach (Nodo n in l)
            {
                s.WriteByte(n.Tipo);
                byte[] k = Encoding.UTF8.GetBytes(n.Clave ?? "");
                s.Write(k, 0, k.Length); s.WriteByte(0);
                if (n.Tipo == 0) { Escribe(s, n.Hijos ?? new List<Nodo>()); s.WriteByte(8); }
                else if (n.Tipo == 1) { s.Write(n.Valor, 0, n.Valor.Length); s.WriteByte(0); }
                else if (n.Tipo == 5) { s.Write(n.Valor, 0, n.Valor.Length); s.WriteByte(0); s.WriteByte(0); }
                else s.Write(n.Valor, 0, n.Valor.Length);
            }
        }
        static byte[] Serializa(List<Nodo> raiz)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                Escribe(ms, raiz);
                ms.WriteByte(8);
                return ms.ToArray();
            }
        }
        static List<Nodo> Lee(byte[] datos) { return new Lector(datos).Mapa(true); }

        static Nodo Txt(string k, string v) { return new Nodo { Tipo = 1, Clave = k, Valor = Encoding.UTF8.GetBytes(v ?? "") }; }
        static Nodo Int(string k, int v) { return new Nodo { Tipo = 2, Clave = k, Valor = BitConverter.GetBytes(v) }; }
        static void PonTxt(Nodo e, string k, string v)
        {
            Nodo n = e.Hijo(k);
            if (n == null) { e.Hijos.Add(Txt(k, v)); return; }
            n.Tipo = 1; n.Valor = Encoding.UTF8.GetBytes(v ?? "");
        }
        static void PonInt(Nodo e, string k, int v)
        {
            Nodo n = e.Hijo(k);
            if (n == null) { e.Hijos.Add(Int(k, v)); return; }
            n.Tipo = 2; n.Valor = BitConverter.GetBytes(v);
        }

        // ------------------------------------------------------------------ donde esta Steam
        public sealed class Estado
        {
            public bool HaySteam, Abierto, Puesto, RutaVieja, Error;
            public string Raiz, Cuenta, Vdf, RutaPuesta, SteamExe, Detalle;
        }

        static bool EsPrueba { get { return Raiz0() != null; } }
        static string Raiz0()
        {
            if (!string.IsNullOrEmpty(RaizPrueba)) return RaizPrueba;
            string e = Environment.GetEnvironmentVariable("CE_STEAM_DIR");
            return string.IsNullOrEmpty(e) ? null : e;
        }

        static string RegTexto(string clave, string valor)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(clave)) { object o = k != null ? k.GetValue(valor) : null; return o != null ? o.ToString() : null; } }
            catch { return null; }
        }
        static int RegInt(string clave, string valor)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(clave)) { object o = k != null ? k.GetValue(valor) : null; return o is int ? (int)o : 0; } }
            catch { return 0; }
        }

        public static string RutaExe { get { return Path.Combine(Paths.AppDir, ExeNombre); } }
        public static string Icono { get { return Path.Combine(Paths.Datos, @"steam\ConsoleEnhancer.ico"); } }

        public static Estado Mira()
        {
            Estado s = new Estado();
            try
            {
                string raiz = Raiz0();
                if (raiz == null)
                {
                    raiz = RegTexto(@"Software\Valve\Steam", "SteamPath");
                    if (raiz != null) raiz = raiz.Replace('/', '\\');
                    s.SteamExe = RegTexto(@"Software\Valve\Steam", "SteamExe");
                    if (s.SteamExe != null) s.SteamExe = s.SteamExe.Replace('/', '\\');
                    else if (raiz != null) s.SteamExe = Path.Combine(raiz, "steam.exe");
                }
                if (raiz == null || !Directory.Exists(Path.Combine(raiz, "userdata"))) { s.Detalle = "Steam no esta instalado (sin userdata)"; return s; }
                s.Raiz = raiz;
                s.Cuenta = Cuenta(raiz);
                if (s.Cuenta == null) { s.Detalle = "Steam sin ningun usuario que haya entrado"; return s; }
                s.HaySteam = true;
                s.Vdf = Path.Combine(raiz, "userdata", s.Cuenta, "config", "shortcuts.vdf");
                s.Abierto = SteamAbierto();
                if (File.Exists(s.Vdf))
                {
                    List<Nodo> r = Lee(File.ReadAllBytes(s.Vdf));
                    Nodo lista = Lista(r, false);
                    if (lista != null)
                        foreach (Nodo e in lista.Hijos)
                            if (EsNuestra(e))
                            {
                                s.Puesto = true;
                                Nodo ex = e.Hijo("Exe");
                                s.RutaPuesta = ex != null ? ex.Texto.Trim('"') : "";
                                s.RutaVieja = !string.Equals(Path.GetFullPath(s.RutaPuesta.Length > 0 ? s.RutaPuesta : "x"), Path.GetFullPath(RutaExe), StringComparison.OrdinalIgnoreCase);
                            }
                }
                s.Detalle = "Steam en " + raiz + ", cuenta " + s.Cuenta + (s.Abierto ? " (abierto)" : " (cerrado)") +
                            (s.Puesto ? ", ConsoleEnhancer puesto (" + s.RutaPuesta + (s.RutaVieja ? ", ruta de otra carpeta" : "") + ")" : ", sin ConsoleEnhancer");
            }
            catch (Exception ex) { s.Error = true; s.Detalle = "no se puede leer el shortcuts.vdf: " + ex.Message; }
            return s;
        }

        // La cuenta: la de Steam abierto (ActiveUser), la ultima que entro (loginusers.vdf MostRecent) o la carpeta tocada mas tarde
        static string Cuenta(string raiz)
        {
            string ud = Path.Combine(raiz, "userdata");
            List<string> hay = new List<string>();
            foreach (string d in Directory.GetDirectories(ud))
            {
                string n = Path.GetFileName(d);
                uint id;
                if (uint.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0) hay.Add(n);
            }
            if (hay.Count == 0) return null;
            if (!EsPrueba)
            {
                int act = RegInt(@"Software\Valve\Steam\ActiveProcess", "ActiveUser");
                if (act != 0 && hay.Contains(((uint)act).ToString(CultureInfo.InvariantCulture))) return ((uint)act).ToString(CultureInfo.InvariantCulture);
            }
            try
            {
                string lu = Path.Combine(raiz, @"config\loginusers.vdf");
                if (File.Exists(lu))
                {
                    string t = File.ReadAllText(lu);
                    foreach (Match m in Regex.Matches(t, "\"(\\d{17})\"\\s*\\{([^}]*)\\}"))
                        if (Regex.IsMatch(m.Groups[2].Value, "\"(?i:mostrecent)\"\\s*\"1\""))
                        {
                            ulong id64 = ulong.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                            string c = (id64 - 76561197960265728UL).ToString(CultureInfo.InvariantCulture);
                            if (hay.Contains(c)) return c;
                        }
                }
            }
            catch { }
            string mejor = null; DateTime t0 = DateTime.MinValue;
            foreach (string c in hay)
            {
                string cfg = Path.Combine(ud, c, "config");
                DateTime t = Directory.Exists(cfg) ? Directory.GetLastWriteTimeUtc(cfg) : Directory.GetLastWriteTimeUtc(Path.Combine(ud, c));
                if (mejor == null || t > t0) { mejor = c; t0 = t; }
            }
            return mejor;
        }

        static bool SteamAbierto()
        {
            if (EsPrueba) return Environment.GetEnvironmentVariable("CE_STEAM_ABIERTO") == "1";
            try { Process[] p = Process.GetProcessesByName("steam"); foreach (Process x in p) x.Dispose(); return p.Length > 0; }
            catch { return false; }
        }

        static Nodo Lista(List<Nodo> raiz, bool crear)
        {
            foreach (Nodo n in raiz) if (n.Tipo == 0 && string.Equals(n.Clave, "shortcuts", StringComparison.OrdinalIgnoreCase)) return n;
            if (!crear) return null;
            Nodo l = new Nodo { Tipo = 0, Clave = "shortcuts", Hijos = new List<Nodo>() };
            raiz.Add(l);
            return l;
        }

        static bool EsNuestra(Nodo e)
        {
            if (e.Tipo != 0 || e.Hijos == null) return false;
            Nodo exe = e.Hijo("Exe"), nom = e.Hijo("AppName") ?? e.Hijo("appname");
            string x = exe != null ? exe.Texto.Trim().Trim('"') : "";
            string fn = "";
            try { fn = Path.GetFileName(x); } catch { }
            return string.Equals(fn, ExeNombre, StringComparison.OrdinalIgnoreCase) ||
                   (nom != null && string.Equals(nom.Texto.Trim(), Nombre, StringComparison.OrdinalIgnoreCase));
        }

        // El appid de un juego ajeno: crc32(exe + nombre) con el bit alto (como lo calcula Steam; luego se queda el que tenga)
        public static int AppId(string exe, string nombre)
        {
            byte[] b = Encoding.UTF8.GetBytes(exe + nombre);
            uint c = 0xFFFFFFFF;
            foreach (byte x in b)
            {
                c ^= x;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
            }
            c = ~c;
            return unchecked((int)(c | 0x80000000));
        }

        static Nodo Entrada(string exe, string dir, string icono)
        {
            Nodo e = new Nodo { Tipo = 0, Clave = "0", Hijos = new List<Nodo>() };
            e.Hijos.Add(Int("appid", AppId(exe, Nombre)));
            e.Hijos.Add(Txt("AppName", Nombre));
            e.Hijos.Add(Txt("Exe", exe));
            e.Hijos.Add(Txt("StartDir", dir));
            e.Hijos.Add(Txt("icon", icono));
            e.Hijos.Add(Txt("ShortcutPath", ""));
            e.Hijos.Add(Txt("LaunchOptions", ""));
            e.Hijos.Add(Int("IsHidden", 0));
            e.Hijos.Add(Int("AllowDesktopConfig", 1));
            e.Hijos.Add(Int("AllowOverlay", 0));
            e.Hijos.Add(Int("OpenVR", 0));
            e.Hijos.Add(Int("Devkit", 0));
            e.Hijos.Add(Txt("DevkitGameID", ""));
            e.Hijos.Add(Int("DevkitOverrideAppID", 0));
            e.Hijos.Add(Int("LastPlayTime", 0));
            e.Hijos.Add(Txt("FlatpakAppID", ""));
            e.Hijos.Add(new Nodo { Tipo = 0, Clave = "tags", Hijos = new List<Nodo>() });
            return e;
        }

        static void Renumera(Nodo lista)
        {
            int i = 0;
            foreach (Nodo e in lista.Hijos) if (e.Tipo == 0) e.Clave = (i++).ToString(CultureInfo.InvariantCulture);
        }

        static bool PonIcono(out string det)
        {
            det = "";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Icono));
                using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Prisma.ico"))
                {
                    if (s == null) { det = "sin el icono en el exe"; return false; }
                    using (MemoryStream ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        byte[] b = ms.ToArray();
                        if (!File.Exists(Icono) || new FileInfo(Icono).Length != b.Length) File.WriteAllBytes(Icono, b);
                    }
                }
                return true;
            }
            catch (Exception ex) { det = ex.Message; return false; }
        }

        // Escribe el vdf nuevo: aparte, comprobado al volver a leerlo, con las copias, y luego en su sitio
        static void Guarda(string vdf, List<Nodo> raiz)
        {
            byte[] nuevo = Serializa(raiz);
            Lee(nuevo);   // (lo que se va a escribir se entiende entero)
            Directory.CreateDirectory(Path.GetDirectoryName(vdf));
            string tmp = vdf + ".ce-nuevo";
            File.WriteAllBytes(tmp, nuevo);
            byte[] vuelta = File.ReadAllBytes(tmp);
            if (vuelta.Length != nuevo.Length) throw new IOException("el archivo nuevo no se escribio entero");
            if (File.Exists(vdf))
            {
                string antes = vdf + ".antes-ce";
                if (!File.Exists(antes)) File.Copy(vdf, antes, false);
                File.Copy(vdf, vdf + ".ce-copia", true);
                File.Copy(tmp, vdf, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, vdf);
        }

        // Pone (o actualiza) ConsoleEnhancer. false con det = por que no
        public static bool Poner(out string det)
        {
            Estado s = Mira();
            if (!s.HaySteam || s.Error) { det = s.Detalle; return false; }
            try
            {
                string icDet;
                bool ic = PonIcono(out icDet);
                string exe = "\"" + RutaExe + "\"", dir = "\"" + Paths.AppDir.TrimEnd('\\') + "\\\"", icono = ic ? Icono : RutaExe;
                List<Nodo> raiz = File.Exists(s.Vdf) ? Lee(File.ReadAllBytes(s.Vdf)) : new List<Nodo>();
                Nodo lista = Lista(raiz, true);
                Nodo nuestra = null;
                List<Nodo> sobran = new List<Nodo>();
                foreach (Nodo e in lista.Hijos) if (EsNuestra(e)) { if (nuestra == null) nuestra = e; else sobran.Add(e); }
                foreach (Nodo e in sobran) lista.Hijos.Remove(e);   // (si alguien la puso dos veces: una)
                string que;
                if (nuestra == null)
                {
                    lista.Hijos.Add(Entrada(exe, dir, icono));
                    que = "anadido";
                }
                else
                {
                    bool igual = nuestra.Hijo("Exe") != null && nuestra.Hijo("Exe").Texto == exe && nuestra.Hijo("StartDir") != null && nuestra.Hijo("StartDir").Texto == dir &&
                                 nuestra.Hijo("icon") != null && nuestra.Hijo("icon").Texto == icono && sobran.Count == 0;
                    if (igual) { det = "ya estaba (" + s.Vdf + ")"; return true; }
                    PonTxt(nuestra, "Exe", exe); PonTxt(nuestra, "StartDir", dir); PonTxt(nuestra, "icon", icono);
                    if (nuestra.Hijo("appid") == null) PonInt(nuestra, "appid", AppId(exe, Nombre));
                    que = "actualizado (la ruta o el icono" + (sobran.Count > 0 ? "; " + sobran.Count + " repetida(s) quitada(s)" : "") + ")";
                }
                Renumera(lista);
                Guarda(s.Vdf, raiz);
                det = que + " en " + s.Vdf + (ic ? "" : " (sin icono propio: " + icDet + ")");
                Log.Write("Steam: ConsoleEnhancer " + det);
                return true;
            }
            catch (Exception ex) { det = "no se ha tocado el shortcuts.vdf: " + ex.Message; Log.Write("Steam: " + det); return false; }
        }

        // Quita las entradas de ConsoleEnhancer (las demas, igual)
        public static bool Quitar(out string det)
        {
            Estado s = Mira();
            if (!s.HaySteam || s.Error) { det = s.Detalle; return false; }
            if (!s.Puesto) { det = "no estaba"; return true; }
            try
            {
                List<Nodo> raiz = Lee(File.ReadAllBytes(s.Vdf));
                Nodo lista = Lista(raiz, false);
                int n = lista.Hijos.RemoveAll(EsNuestra);
                Renumera(lista);
                Guarda(s.Vdf, raiz);
                det = "quitado (" + n + ") de " + s.Vdf;
                Log.Write("Steam: ConsoleEnhancer " + det);
                return true;
            }
            catch (Exception ex) { det = "no se ha tocado el shortcuts.vdf: " + ex.Message; Log.Write("Steam: " + det); return false; }
        }

        // Cerrar Steam (como su "Salir") y esperar; abrirlo
        public static bool Cerrar(Estado s, int esperaMs)
        {
            if (EsPrueba || !s.Abierto) return true;
            try
            {
                Log.Write("Steam: se cierra para que lea el acceso");
                Process.Start(new ProcessStartInfo(s.SteamExe, "-shutdown") { UseShellExecute = false });
                Stopwatch sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < esperaMs) { if (!SteamAbierto()) { Thread.Sleep(1500); return true; } Thread.Sleep(300); }
            }
            catch (Exception ex) { Log.Error("Steam: cerrar", ex); }
            return !SteamAbierto();
        }
        public static void Abrir(Estado s)
        {
            if (EsPrueba || s.SteamExe == null || !File.Exists(s.SteamExe)) return;
            try { Process.Start(new ProcessStartInfo(s.SteamExe) { UseShellExecute = true }); Log.Write("Steam: abierto otra vez"); }
            catch (Exception ex) { Log.Error("Steam: abrir", ex); }
        }

        // Al abrir la app: con steam_acceso=1 y Steam cerrado, la ruta al dia (una version nueva en otra carpeta)
        public static void AlAbrirApp()
        {
            try
            {
                if (Ce.IniLeer("steam_acceso") != "1") return;
                Estado s = Mira();
                if (!s.HaySteam || s.Error || !s.Puesto || !s.RutaVieja) return;
                if (s.Abierto) { Log.Write("Steam: el acceso de ConsoleEnhancer apunta a " + s.RutaPuesta + " (Steam abierto: se corrige desde el panel de ajustes o con Steam cerrado)"); return; }
                string det;
                Poner(out det);
            }
            catch (Exception ex) { Log.Error("Steam: al abrir", ex); }
        }

        // ------------------------------------------------------------------ prueba: --test-steam[=<informe>][;<carpeta>[;<shortcuts.vdf a copiar>]]
        public static int Prueba(string arg)
        {
            string informe = null, carpeta = null, real = null;
            if (!string.IsNullOrEmpty(arg))
            {
                string[] p = arg.Split(';');
                if (p[0].Length > 0) informe = p[0];
                if (p.Length > 1 && p[1].Length > 0) carpeta = p[1];
                if (p.Length > 2 && p[2].Length > 0) real = p[2];
            }
            if (carpeta == null) carpeta = Path.Combine(Path.GetTempPath(), "ce_test_steam_" + Process.GetCurrentProcess().Id);
            StringBuilder sb = new StringBuilder();
            int fallos = 0, total = 0;
            Action<string, bool> check = delegate (string que, bool ok) { total++; if (!ok) fallos++; sb.AppendLine((ok ? "bien   " : "FALLO  ") + que); };
            string realSteam = null;
            try { realSteam = (RegTexto(@"Software\Valve\Steam", "SteamPath") ?? "").Replace('/', '\\'); } catch { }
            try
            {
                string raiz = Path.Combine(carpeta, "Steam");
                if (realSteam != null && realSteam.Length > 0 && Path.GetFullPath(raiz).StartsWith(Path.GetFullPath(realSteam), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("la carpeta de prueba esta dentro del Steam de verdad");
                if (Directory.Exists(raiz)) Directory.Delete(raiz, true);
                string cfg = Path.Combine(raiz, @"userdata\12345678\config");
                Directory.CreateDirectory(cfg);
                Directory.CreateDirectory(Path.Combine(raiz, @"userdata\0"));
                RaizPrueba = raiz;
                string vdf = Path.Combine(cfg, "shortcuts.vdf");
                string exe = "\"" + RutaExe + "\"";

                Estado s = Mira();
                check("carpeta falsa: cuenta 12345678, sin vdf, sin poner (" + s.Detalle + ")", s.HaySteam && s.Cuenta == "12345678" && !s.Puesto && !File.Exists(vdf));
                check("el vdf es el de la carpeta falsa, nunca el de verdad", s.Vdf != null && s.Vdf.StartsWith(raiz, StringComparison.OrdinalIgnoreCase));

                // 1. sin shortcuts.vdf: se crea con una entrada
                string det;
                bool ok = Poner(out det);
                List<Nodo> r = Lee(File.ReadAllBytes(vdf));
                Nodo l = Lista(r, false);
                Nodo e = l != null && l.Hijos.Count == 1 ? l.Hijos[0] : null;
                check("1. sin vdf: creado con 1 entrada (" + det + ")", ok && e != null && e.Clave == "0");
                check("1. campos: AppName, Exe entre comillas, StartDir, icono que existe, overlay apagado", e != null && e.Hijo("AppName").Texto == Nombre && e.Hijo("Exe").Texto == exe &&
                      e.Hijo("StartDir").Texto.StartsWith("\"") && File.Exists(e.Hijo("icon").Texto) && BitConverter.ToInt32(e.Hijo("AllowOverlay").Valor, 0) == 0);
                int appid = e != null ? BitConverter.ToInt32(e.Hijo("appid").Valor, 0) : 0;
                check("1. appid crc32(exe+nombre) con el bit alto (" + ((uint)appid).ToString("X8") + ")", appid == AppId(exe, Nombre) && appid < 0);
                // 2. otra vez: sin duplicar
                byte[] antes = File.ReadAllBytes(vdf);
                ok = Poner(out det);
                check("2. otra vez: no se duplica y no se reescribe (" + det + ")", ok && Lista(Lee(File.ReadAllBytes(vdf)), false).Hijos.Count == 1 && Igual(antes, File.ReadAllBytes(vdf)));
                ok = Quitar(out det);
                check("2. quitar: lista vacia (" + det + ")", ok && Lista(Lee(File.ReadAllBytes(vdf)), false).Hijos.Count == 0 && File.Exists(vdf + ".ce-copia") && File.Exists(vdf + ".antes-ce"));

                // 3. un vdf con dos juegos ajenos (y tipos raros: float, uint64, etiquetas): se anaden y se quitan y queda byte a byte
                List<Nodo> ajeno = new List<Nodo>();
                Nodo la = Lista(ajeno, true);
                Nodo a1 = Entrada("\"C:\\Juegos\\Otro\\juego.exe\"", "\"C:\\Juegos\\Otro\\\"", "");
                a1.Hijo("AppName").Valor = Encoding.UTF8.GetBytes("Juego ñandú 日本");
                a1.Hijo("tags").Hijos.Add(Txt("0", "favorite"));
                a1.Hijos.Add(new Nodo { Tipo = 3, Clave = "flotante", Valor = BitConverter.GetBytes(1.5f) });
                a1.Hijos.Add(new Nodo { Tipo = 7, Clave = "grande", Valor = BitConverter.GetBytes(123456789012345UL) });
                Nodo a2 = Entrada("\"D:\\Emu\\emu.exe\"", "\"D:\\Emu\\\"", "D:\\Emu\\emu.ico");
                a2.Hijo("AppName").Valor = Encoding.UTF8.GetBytes("Emu");
                la.Hijos.Add(a1); la.Hijos.Add(a2); Renumera(la);
                byte[] orig = Serializa(ajeno);
                File.WriteAllBytes(vdf, orig);
                check("3. lectura y escritura sin cambios byte a byte", Igual(orig, Serializa(Lee(orig))));
                ok = Poner(out det);
                l = Lista(Lee(File.ReadAllBytes(vdf)), false);
                check("3. con 2 ajenos: 3 entradas, la nuestra la 2 (" + det + ")", ok && l.Hijos.Count == 3 && EsNuestra(l.Hijos[2]) && l.Hijos[2].Clave == "2" && !EsNuestra(l.Hijos[0]) && !EsNuestra(l.Hijos[1]));
                check("3. copia de seguridad = el de antes", Igual(File.ReadAllBytes(vdf + ".ce-copia"), orig));
                ok = Quitar(out det);
                check("3. quitar: igual byte a byte que el de antes (" + det + ")", ok && Igual(File.ReadAllBytes(vdf), orig));

                // 4. la de una version vieja en otra carpeta, en medio: se actualiza en su sitio con su appid; y una repetida se quita
                List<Nodo> viejo = Lee(orig);
                Nodo lv = Lista(viejo, false);
                Nodo v1 = Entrada("\"C:\\Users\\x\\Downloads\\ConsoleEnhancer-v1.3.2\\ConsoleEnhancer.exe\"", "\"C:\\Users\\x\\Downloads\\ConsoleEnhancer-v1.3.2\\\"", "");
                PonInt(v1, "appid", -12345);
                lv.Hijos.Insert(1, v1);
                Nodo v2 = Entrada("\"E:\\ce\\ConsoleEnhancer.exe\"", "\"E:\\ce\\\"", "");
                lv.Hijos.Add(v2);
                Renumera(lv);
                File.WriteAllBytes(vdf, Serializa(viejo));
                s = Mira();
                check("4. ruta de otra carpeta vista (" + s.Detalle + ")", s.Puesto && s.RutaVieja);
                ok = Poner(out det);
                l = Lista(Lee(File.ReadAllBytes(vdf)), false);
                int nuestras = 0; foreach (Nodo x in l.Hijos) if (EsNuestra(x)) nuestras++;
                check("4. actualizada en su sitio, 1 sola, su appid de siempre, ruta nueva (" + det + ")", ok && l.Hijos.Count == 3 && nuestras == 1 && EsNuestra(l.Hijos[1]) &&
                      BitConverter.ToInt32(l.Hijos[1].Hijo("appid").Valor, 0) == -12345 && l.Hijos[1].Hijo("Exe").Texto == exe && l.Hijos[2].Clave == "2");
                s = Mira();
                check("4. ya no es de otra carpeta", s.Puesto && !s.RutaVieja);

                // 5. un vdf roto (cortado / tipo desconocido): no se toca
                byte[] roto = new byte[orig.Length - 7]; Buffer.BlockCopy(orig, 0, roto, 0, roto.Length);
                File.WriteAllBytes(vdf, roto);
                ok = Poner(out det);
                check("5. cortado: no se toca (" + det + ")", !ok && Igual(File.ReadAllBytes(vdf), roto));
                byte[] raro = (byte[])orig.Clone(); raro[Array.IndexOf(raro, (byte)2, 14)] = 0x09;
                File.WriteAllBytes(vdf, raro);
                ok = Poner(out det);
                check("5. tipo desconocido: no se toca (" + det + ")", !ok && Igual(File.ReadAllBytes(vdf), raro));

                // 6. Steam abierto (simulado): Mira lo dice; nunca se cierra ni se abre nada en la prueba
                Environment.SetEnvironmentVariable("CE_STEAM_ABIERTO", "1");
                File.WriteAllBytes(vdf, orig);
                s = Mira();
                check("6. Steam abierto se ve y Cerrar no toca procesos en la prueba", s.Abierto && Cerrar(s, 10));
                Environment.SetEnvironmentVariable("CE_STEAM_ABIERTO", null);

                // 7. loginusers.vdf: la cuenta de MostRecent entre varias
                Directory.CreateDirectory(Path.Combine(raiz, @"userdata\87654321\config"));
                Directory.CreateDirectory(Path.Combine(raiz, "config"));
                ulong id64 = 76561197960265728UL + 12345678UL;
                File.WriteAllText(Path.Combine(raiz, @"config\loginusers.vdf"), "\"users\"\n{\n\t\"" + (76561197960265728UL + 87654321UL) + "\"\n\t{\n\t\t\"AccountName\"\t\t\"b\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t}\n\t\"" + id64 + "\"\n\t{\n\t\t\"AccountName\"\t\t\"a\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}\n");
                Directory.SetLastWriteTimeUtc(Path.Combine(raiz, @"userdata\87654321\config"), DateTime.UtcNow.AddMinutes(5));
                s = Mira();
                check("7. varias cuentas: la de MostRecent (" + s.Cuenta + ")", s.Cuenta == "12345678");

                // 8. una copia de un shortcuts.vdf de verdad (si se da): se lee, se escribe igual, poner y quitar lo deja igual
                if (real != null && File.Exists(real))
                {
                    byte[] rb = File.ReadAllBytes(real);
                    File.WriteAllBytes(vdf, rb);
                    bool lee = false; try { lee = Igual(Serializa(Lee(rb)), rb); } catch (Exception ex) { sb.AppendLine("       (" + ex.Message + ")"); }
                    check("8. copia de verdad (" + rb.Length + " bytes): se lee y se reescribe byte a byte", lee);
                    s = Mira();
                    bool yaEstaba = s.Puesto;
                    ok = Poner(out det);
                    bool ok2 = Quitar(out det);
                    check("8. copia de verdad: poner y quitar" + (yaEstaba ? " (ya tenia ConsoleEnhancer: no se compara)" : " la deja igual"), ok && ok2 && (yaEstaba || Igual(File.ReadAllBytes(vdf), rb)));
                }
                else sb.AppendLine("       (sin copia de un shortcuts.vdf de verdad)");
            }
            catch (Exception ex) { check("excepcion: " + ex, false); }
            finally
            {
                RaizPrueba = null;
                try { Directory.Delete(Path.Combine(carpeta, "Steam"), true); } catch { }
            }
            sb.Insert(0, "Anadir a Steam: " + (total - fallos) + " de " + total + " bien\r\n");
            string txt = sb.ToString();
            if (informe != null) File.WriteAllText(informe, txt, new UTF8Encoding(false));
            Console.Write(txt);
            return fallos;
        }

        static bool Igual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
