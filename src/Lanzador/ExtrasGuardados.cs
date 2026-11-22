// ExtrasGuardados.cs - lo que pone el usuario (y no va en el zip) guardado fuera de la carpeta de la app (22-11-2026, 1.2.3, agente MOTOR5)
//
// El usuario baja cada version nueva limpia del zip a una carpeta nueva y borra la vieja: la 1.2.2 arranco "sin nvngx_dlssnr.dll: DLSS 5
// no disponible" y tuvo que volver a añadirlo. Lo suyo (extras\nvngx_dlssnr.dll y
// extras\runtimes\dlssg_sm86; hasta la 1.2.3 tambien engine\PrismaNr.dll) se guarda tambien en %LOCALAPPDATA%\ConsoleEnhancer\extras (donde ya miran la
// deteccion -OpcionesDeteccion.RutasExtra- y Componentes.Sm86EnElPc) al añadirlo y al verlo en su sitio al arrancar, y una version nueva
// que no lo tiene lo recoge de ahi (o de otra version de la app al lado, como HeredarDatos) antes de Ce.PrepararExtras, comprobado:
// DLL de 64 bits y su tamaño (el apuntado al guardarla), dlssg_sm86 completo y el que pide el build-manifest.json de Magpie
// (Componentes.Sm86Vale). Lo dice en el log. Si en ESTA carpeta (y este exe) ya estuvo y ahora falta, lo ha quitado el usuario: no se
// recoge. "Quitar DLSS 5" lo quita tambien de aqui; la desinstalacion con "Sí = se borra todo", tambien (Desinstalar.cs).
// Nada de esto va nunca a git ni al zip (esta fuera de la carpeta de la app). Pruebas: CE_EXTRAS_GUARDADOS=<carpeta> en vez de la de
// %LOCALAPPDATA% (y entonces tambien con --datos=).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    static class ExtrasGuardados
    {
        public static string Dir
        {
            get
            {
                string e = Environment.GetEnvironmentVariable("CE_EXTRAS_GUARDADOS");
                return !string.IsNullOrEmpty(e) ? e : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ConsoleEnhancer\extras");
            }
        }
        static bool EnPrueba { get { return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CE_EXTRAS_GUARDADOS")); } }
        static string Manifiesto { get { return Path.Combine(Dir, "guardado.txt"); } }
        static readonly object sync = new object();

        // Lo que se guarda: nombre, donde va en la app, donde se guarda, si es carpeta, tamaño minimo de la DLL
        sealed class Cosa
        {
            public string Nombre, EnApp, Guardado; public bool Carpeta; public long Minimo;
        }
        static Cosa Dlss5 { get { return new Cosa { Nombre = Integrado.Dlss5Dll, EnApp = Extras.Dll, Guardado = Path.Combine(Dir, Integrado.Dlss5Dll), Minimo = 8L << 20 }; } }
        static Cosa Sm86 { get { return new Cosa { Nombre = "dlssg_sm86", EnApp = Extras.Sm86, Guardado = Path.Combine(Dir, @"runtimes\dlssg_sm86"), Carpeta = true }; } }
        // (1.2.3 guardaba tambien engine\PrismaNr.dll; 1.2.4 SOLOMAGPIE: DLSS 5 ya no va en el motor: ni se guarda ni se recoge. La que haya
        // en %LOCALAPPDATA%\ConsoleEnhancer\extras\engine se queda sin usar: la borra "Desinstalar" con "Sí = se borra todo")
        static Cosa[] Todas { get { return new[] { Dlss5, Sm86 }; } }

        // (como Integrado.CopiaSeguridad: nada en pruebas con otra carpeta de datos o simulando, ni en la espera y sus ayudantes)
        static bool Toca()
        {
            if (EnPrueba) return true;
            foreach (string a in Environment.GetCommandLineArgs())
                if (a.StartsWith("--datos=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--simula", StringComparison.OrdinalIgnoreCase) ||
                    a.StartsWith("--prueba", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--test", StringComparison.OrdinalIgnoreCase) ||
                    a == "--espera" || a.StartsWith("--ce-", StringComparison.OrdinalIgnoreCase) || a == "--mando-pc") return false;
            return true;
        }

        // ---- al arrancar (Ce.Arranque, antes de PrepararExtras): recoger lo que falta y guardar lo que esta ----
        public static void AlArrancar()
        {
            if (!Toca()) return;
            lock (sync)
            {
                Dictionary<string, string> m = Leer();
                foreach (Cosa c in Todas)
                {
                    try { if (!Esta(c)) Recoger(c, m); }
                    catch (Exception ex) { Log.Error("Extras guardados: recogiendo " + c.Nombre, ex); }
                }
                foreach (Cosa c in Todas)
                {
                    try { if (Esta(c)) GuardarUna(c, m, "visto en su sitio"); }
                    catch (Exception ex) { Log.Error("Extras guardados: guardando " + c.Nombre, ex); }
                }
                Escribir(m);
            }
        }

        // ---- al añadirlo ("Añadir DLSS 5": la DLL y su dlssg_sm86 si estaba al lado) ----
        public static void Guardar(string porque)
        {
            if (!Toca()) return;
            lock (sync)
            {
                Dictionary<string, string> m = Leer();
                foreach (Cosa c in Todas)
                    try { if (Esta(c)) GuardarUna(c, m, porque); }
                    catch (Exception ex) { Log.Error("Extras guardados: guardando " + c.Nombre, ex); }
                Escribir(m);
            }
        }

        // ---- "Quitar DLSS 5": fuera tambien de aqui (si no, la proxima version lo recogeria) ----
        public static void QuitarDlss5()
        {
            if (!Toca()) return;
            lock (sync)
            {
                Dictionary<string, string> m = Leer();
                foreach (Cosa c in new[] { Dlss5, Sm86 })
                {
                    try
                    {
                        if (c.Carpeta) { if (Directory.Exists(c.Guardado)) Directory.Delete(c.Guardado, true); }
                        else if (File.Exists(c.Guardado)) File.Delete(c.Guardado);
                        m.Remove("tam|" + c.Nombre);
                    }
                    catch (Exception ex) { Log.Error("Extras guardados: quitando " + c.Nombre, ex); }
                }
                Escribir(m);
                Log.Write("Extras guardados: " + Integrado.Dlss5Dll + " y dlssg_sm86 quitados tambien de " + Dir + " (una version nueva ya no los recoge)");
            }
        }

        static bool Esta(Cosa c)
        {
            if (!c.Carpeta) return File.Exists(c.EnApp);
            return Directory.Exists(Extras.Sm86) || Directory.Exists(Extras.Sm86Viejo);
        }

        // La marca de "esta carpeta de la app (y este exe) ya lo tenia": si falta, lo ha quitado el usuario
        static string ClaveCopia(Cosa c)
        {
            long t = 0;
            try { t = File.GetLastWriteTimeUtc(Path.Combine(Paths.AppDir, "ConsoleEnhancer.exe")).Ticks; } catch { }
            return "visto|" + Path.GetFullPath(Paths.AppDir).TrimEnd('\\').ToLowerInvariant() + "|" + t.ToString(CultureInfo.InvariantCulture) + "|" + c.Nombre;
        }

        // (solo lo que vale; y solo entonces "esta copia lo tuvo": lo que quita la propia app -un dlssg_sm86 de otra version- no cuenta
        // como quitado por el usuario)
        static void GuardarUna(Cosa c, Dictionary<string, string> m, string porque)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (c.Carpeta)
            {
                string de = Directory.Exists(Extras.Sm86) ? Extras.Sm86 : Extras.Sm86Viejo;
                // (lo mismo que ya se guardo -comprobado entonces-: sin volver a calcular sus ~28 MB de SHA-256 en cada arranque)
                if (Sm86Iguales(de, c.Guardado)) { m[ClaveCopia(c)] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); return; }
                if (!Componentes.Sm86Vale(de)) return;   // (a medias o de otra version: Componentes.AsegurarSm86 pone la buena)
                m[ClaveCopia(c)] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                int n = Componentes.CopiarSm86(de, c.Guardado);
                Log.Write("Extras guardados: dlssg_sm86 (" + n + " ficheros) guardado en " + c.Guardado + " (" + porque + "; " + sw.ElapsedMilliseconds + " ms): una version nueva lo recoge sola");
                return;
            }
            string mal = Extras.MirarDll(c.EnApp);
            long tam = new FileInfo(c.EnApp).Length;
            if (mal != null || tam < c.Minimo) { Log.Write("Extras guardados: " + c.Nombre + " de la app no se guarda (" + (mal ?? tam + " bytes, muy pequeña") + ")"); return; }
            m[ClaveCopia(c)] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (Igual(c.EnApp, c.Guardado)) { m["tam|" + c.Nombre] = tam.ToString(CultureInfo.InvariantCulture); return; }
            CopiarDll(c.EnApp, c.Guardado);
            m["tam|" + c.Nombre] = tam.ToString(CultureInfo.InvariantCulture);
            Log.Write("Extras guardados: " + c.Nombre + " (" + tam + " bytes) guardada en " + c.Guardado + " (" + porque + "; " + sw.ElapsedMilliseconds + " ms): una version nueva la recoge sola");
        }

        static void Recoger(Cosa c, Dictionary<string, string> m)
        {
            if (m.ContainsKey(ClaveCopia(c)))
            {
                Log.Write("Extras guardados: " + c.Nombre + " ya estuvo en esta carpeta de la app y no esta: la has quitado tu, no se recoge (para volver a ponerla: Añadir DLSS 5)");
                return;
            }
            // de donde: lo guardado y, si no vale o no esta, otra version de la app al lado (la mas nueva que valga)
            List<string> de = new List<string>();
            de.Add(c.Guardado);
            try
            {
                string app = Path.GetFullPath(Paths.AppDir).TrimEnd('\\'), padre = Path.GetDirectoryName(app);
                string rel = c.Carpeta ? @"extras\runtimes\dlssg_sm86" : @"extras\" + c.Nombre;
                if (!string.IsNullOrEmpty(padre) && Directory.Exists(padre))
                    foreach (string dir in Directory.GetDirectories(padre, "ConsoleEnhancer*"))
                        if (!string.Equals(dir.TrimEnd('\\'), app, StringComparison.OrdinalIgnoreCase)) de.Add(Path.Combine(dir, rel));
            }
            catch { }
            string mejor = null, porQue = "";
            DateTime cuando = DateTime.MinValue;
            foreach (string d in de)
            {
                string no = NoVale(c, d, d == c.Guardado ? m : null);
                if (no != null) { if (d == c.Guardado && (c.Carpeta ? Directory.Exists(d) : File.Exists(d))) porQue = no; continue; }
                DateTime w = File.GetLastWriteTimeUtc(c.Carpeta ? Path.Combine(d, "version.dll") : d);
                if (d == c.Guardado) { mejor = d; break; }   // (lo guardado va primero)
                if (mejor == null || w > cuando) { mejor = d; cuando = w; }
            }
            if (mejor == null)
            {
                if (porQue.Length > 0) Log.Write("Extras guardados: " + c.Nombre + " guardada en " + c.Guardado + " no vale (" + porQue + "): no se recoge");
                return;
            }
            Stopwatch sw = Stopwatch.StartNew();
            if (c.Carpeta)
            {
                int n = Componentes.CopiarSm86(mejor, c.EnApp);
                Log.Write("Extras guardados: dlssg_sm86 que faltaba recogido de " + mejor + " (" + n + " ficheros, " + sw.ElapsedMilliseconds + " ms; de la comunidad, DLL autofirmadas, no oficial)");
            }
            else
            {
                CopiarDll(mejor, c.EnApp);
                Log.Write("Extras guardados: " + c.Nombre + " que faltaba recogida de " + mejor + " (" + new FileInfo(c.EnApp).Length + " bytes, " + sw.ElapsedMilliseconds + " ms" +
                          "; DLL de NVIDIA modificada por la comunidad, SIN FIRMA, no oficial)");
            }
            m[ClaveCopia(c)] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        // null = vale; si no, por que (m: el tamaño apuntado al guardarla, solo para lo guardado)
        static string NoVale(Cosa c, string d, Dictionary<string, string> m)
        {
            if (c.Carpeta)
            {
                if (!Directory.Exists(d)) return "no esta";
                return Componentes.Sm86Vale(d) ? null : "incompleto o de otra version (no es el que pide Magpie)";
            }
            if (!File.Exists(d)) return "no esta";
            string mal = Extras.MirarDll(d);
            if (mal != null) return mal;
            long tam = new FileInfo(d).Length;
            if (tam < c.Minimo) return tam + " bytes, muy pequeña";
            string t;
            if (m != null && m.TryGetValue("tam|" + c.Nombre, out t) && t != tam.ToString(CultureInfo.InvariantCulture)) return tam + " bytes y al guardarla tenia " + t;
            return null;
        }

        static readonly string[] Sm86Ficheros = { "version.dll", @"dlssg_sm86\runtime\nvngx_dlssg.dll", @"dlssg_sm86\backend\sm86_backend.dll", @"dlssg_sm86\kernels\fg_kernels.dll" };
        static bool Sm86Iguales(string a, string b)
        {
            foreach (string f in Sm86Ficheros) if (!Igual(Path.Combine(a, f), Path.Combine(b, f))) return false;
            return true;
        }

        static bool Igual(string a, string b)
        {
            try
            {
                FileInfo fa = new FileInfo(a), fb = new FileInfo(b);
                return fa.Exists && fb.Exists && fa.Length == fb.Length && fa.LastWriteTimeUtc == fb.LastWriteTimeUtc;
            }
            catch { return false; }
        }

        // A un .parcial y luego en su sitio (una copia cortada no queda con el nombre bueno); la fecha del original (File.Copy la conserva)
        static void CopiarDll(string de, string a)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(a));
            string tmp = a + ".parcial";
            File.Copy(de, tmp, true);
            if (new FileInfo(tmp).Length != new FileInfo(de).Length) { File.Delete(tmp); throw new IOException("copia incompleta de " + de); }
            if (File.Exists(a)) File.Delete(a);
            File.Move(tmp, a);
        }

        // guardado.txt: "clave<TAB>valor" (tam|<nombre> = tamaño al guardarla; visto|<carpeta>|<exe>|<nombre> = esa copia ya lo tuvo)
        static Dictionary<string, string> Leer()
        {
            Dictionary<string, string> m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(Manifiesto)) return m;
                foreach (string l in File.ReadAllLines(Manifiesto))
                {
                    if (l.Length == 0 || l[0] == '#') continue;
                    int i = l.IndexOf('\t');
                    if (i > 0) m[l.Substring(0, i)] = l.Substring(i + 1);
                }
            }
            catch { }
            return m;
        }

        static void Escribir(Dictionary<string, string> m)
        {
            try
            {
                if (m.Count == 0 && !File.Exists(Manifiesto)) return;
                Directory.CreateDirectory(Dir);
                StringBuilder sb = new StringBuilder("# ConsoleEnhancer: lo que pusiste tu (no va en el zip), para que una version nueva lo recoja sola. Borrar esta carpeta = olvidarlo\r\n");
                foreach (KeyValuePair<string, string> kv in m) sb.Append(kv.Key).Append('\t').Append(kv.Value).Append("\r\n");
                File.WriteAllText(Manifiesto, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("Extras guardados: " + Manifiesto, ex); }
        }
    }
}
