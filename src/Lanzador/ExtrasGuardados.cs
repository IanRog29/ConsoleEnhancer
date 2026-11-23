// ExtrasGuardados.cs - lo que pone el usuario (y no va en el zip) guardado fuera de la carpeta de la app (22-11-2026, 1.2.3, agente MOTOR5)
//
// El usuario baja cada version nueva limpia del zip a una carpeta nueva y borra la vieja: la 1.2.2 arranco "sin nvngx_dlssnr.dll: DLSS 5
// no disponible" y tuvo que volver a añadirlo; con engine\PrismaNr.dll pasaria igual. Lo suyo (extras\nvngx_dlssnr.dll,
// extras\runtimes\dlssg_sm86, engine\PrismaNr.dll y, desde la 1.2.4, engine\PrismaFg.dll) se guarda tambien en %LOCALAPPDATA%\ConsoleEnhancer\extras (donde ya miran la
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
                if (!string.IsNullOrEmpty(e)) return e;
                // (1.3.1, AJUSTES: con CE_PC_DIR -pruebas de la carpeta del PC, AjustesPc.cs- tambien extras\ en esa)
                if (AjustesPc.PcDePrueba) return Path.Combine(AjustesPc.PcBase, "extras");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ConsoleEnhancer\extras");
            }
        }
        static bool EnPrueba { get { return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CE_EXTRAS_GUARDADOS")) || AjustesPc.PcDePrueba; } }
        static string Manifiesto { get { return Path.Combine(Dir, "guardado.txt"); } }
        static readonly object sync = new object();

        // Lo que se guarda: nombre, donde va en la app, donde se guarda, si es carpeta, tamaño minimo de la DLL
        sealed class Cosa
        {
            public string Nombre, EnApp, Guardado; public long Minimo;
        }
        static Cosa Dlss5 { get { return new Cosa { Nombre = Integrado.Dlss5Dll, EnApp = Extras.Dll, Guardado = Path.Combine(Dir, Integrado.Dlss5Dll), Minimo = 8L << 20 }; } }
        static Cosa Prisma { get { return new Cosa { Nombre = "PrismaNr.dll", EnApp = Extras.PrismaNr, Guardado = Path.Combine(Dir, @"engine\PrismaNr.dll"), Minimo = 16L << 10 }; } }
        // (1.2.4, ultima decision: PrismaNr.dll viene ya en el zip -engine\-: no se guarda ni se recoge, para que una version nueva no coja
        // una copia vieja. Solo lo que pone el usuario: su nvngx_dlssnr.dll. 1.2.4 ronda 6, QUITAFG: y ya no su dlssg_sm86 -era para el DLSS FG
        // de Magpie, que ya no esta-; lo guardado de antes en runtimes\dlssg_sm86 de esta carpeta se queda donde esta)
        static Cosa[] Todas { get { return new[] { Dlss5 }; } }

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

        // ---- al añadirlo ("Añadir DLSS 5": la DLL y PrismaNr.dll si estaba al lado) ----
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
                foreach (Cosa c in new[] { Dlss5 })
                {
                    try
                    {
                        if (File.Exists(c.Guardado)) File.Delete(c.Guardado);
                        m.Remove("tam|" + c.Nombre);
                    }
                    catch (Exception ex) { Log.Error("Extras guardados: quitando " + c.Nombre, ex); }
                }
                Escribir(m);
                Log.Write("Extras guardados: " + Integrado.Dlss5Dll + " quitada tambien de " + Dir + " (una version nueva ya no la recoge)");
            }
        }

        static bool Esta(Cosa c)
        {
            return File.Exists(c.EnApp);
        }

        // La marca de "esta carpeta de la app (y este exe) ya lo tenia": si falta, lo ha quitado el usuario
        static string ClaveCopia(Cosa c)
        {
            long t = 0;
            try { t = File.GetLastWriteTimeUtc(Path.Combine(Paths.AppDir, "ConsoleEnhancer.exe")).Ticks; } catch { }
            return "visto|" + Path.GetFullPath(Paths.AppDir).TrimEnd('\\').ToLowerInvariant() + "|" + t.ToString(CultureInfo.InvariantCulture) + "|" + c.Nombre;
        }

        // (solo lo que vale; y solo entonces "esta copia lo tuvo")
        static void GuardarUna(Cosa c, Dictionary<string, string> m, string porque)
        {
            Stopwatch sw = Stopwatch.StartNew();
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
                string rel = c.Nombre.StartsWith("Prisma", StringComparison.OrdinalIgnoreCase) ? @"engine\" + c.Nombre : @"extras\" + c.Nombre;
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
                if (no != null) { if (d == c.Guardado && File.Exists(d)) porQue = no; continue; }
                DateTime w = File.GetLastWriteTimeUtc(d);
                if (d == c.Guardado) { mejor = d; break; }   // (lo guardado va primero)
                if (mejor == null || w > cuando) { mejor = d; cuando = w; }
            }
            if (mejor == null)
            {
                if (porQue.Length > 0) Log.Write("Extras guardados: " + c.Nombre + " guardada en " + c.Guardado + " no vale (" + porQue + "): no se recoge");
                return;
            }
            Stopwatch sw = Stopwatch.StartNew();
            CopiarDll(mejor, c.EnApp);
            Log.Write("Extras guardados: " + c.Nombre + " que faltaba recogida de " + mejor + " (" + new FileInfo(c.EnApp).Length + " bytes, " + sw.ElapsedMilliseconds + " ms" +
                      (c.Nombre.StartsWith("Prisma", StringComparison.OrdinalIgnoreCase) ? "; pieza propia del motor" : "; DLL de NVIDIA modificada por la comunidad, SIN FIRMA, no oficial") + ")");
            m[ClaveCopia(c)] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        // null = vale; si no, por que (m: el tamaño apuntado al guardarla, solo para lo guardado)
        static string NoVale(Cosa c, string d, Dictionary<string, string> m)
        {
            if (!File.Exists(d)) return "no esta";
            string mal = Extras.MirarDll(d);
            if (mal != null) return mal;
            long tam = new FileInfo(d).Length;
            if (tam < c.Minimo) return tam + " bytes, muy pequeña";
            string t;
            if (m != null && m.TryGetValue("tam|" + c.Nombre, out t) && t != tam.ToString(CultureInfo.InvariantCulture)) return tam + " bytes y al guardarla tenia " + t;
            return null;
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

        // --test-extras-guardados[=<informe>]: BuscarPrismaNr con carpetas de prueba en %TEMP% (sin la app ni la grafica)
        public static int Prueba(string informe)
        {
            StringBuilder sb = new StringBuilder("# MOTOR5: PrismaNr.dll junto a la DLL elegida (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n");
            int fallos = 0;
            string raiz = Path.Combine(Path.GetTempPath(), "ce_test_prismanr_" + Process.GetCurrentProcess().Id);
            try
            {
                byte[] dll = File.ReadAllBytes(typeof(ExtrasGuardados).Assembly.Location);   // (cualquier binario para las rutas; MirarDll se prueba aparte)
                string[][] casos = {
                    new[] { @"a\nvngx_dlssnr.dll", @"a\PrismaNr.dll", @"a\PrismaNr.dll" },
                    new[] { @"b\x\nvngx_dlssnr.dll", @"b\engine\PrismaNr.dll", @"b\engine\PrismaNr.dll" },
                    new[] { @"c\x\y\nvngx_dlssnr.dll", @"c\PrismaNr.dll", @"c\PrismaNr.dll" },
                    new[] { @"d\x\y\z\nvngx_dlssnr.dll", @"d\PrismaNr.dll", "" },   // (tres carpetas mas arriba: no)
                    new[] { @"e\nvngx_dlssnr.dll", "", "" } };
                foreach (string[] k in casos)
                {
                    string dl = Path.Combine(raiz, k[0]);
                    Directory.CreateDirectory(Path.GetDirectoryName(dl)); File.WriteAllBytes(dl, dll);
                    if (k[1].Length > 0) { string p = Path.Combine(raiz, k[1]); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, dll); }
                    string r = Extras.BuscarPrismaNr(dl) ?? "";
                    string esp = k[2].Length > 0 ? Path.Combine(raiz, k[2]) : "";
                    bool ok = string.Equals(r, esp, StringComparison.OrdinalIgnoreCase);
                    if (!ok) fallos++;
                    sb.AppendLine((ok ? "  ok    " : "  FALLO ") + k[0] + " -> " + (r.Length > 0 ? r.Substring(raiz.Length + 1) : "(nada)") + (ok ? "" : " (esperado " + (esp.Length > 0 ? k[2] : "nada") + ")"));
                }
                bool mOk = Extras.MirarDll(typeof(ExtrasGuardados).Assembly.Location) != null;   // (el exe de la app no es una DLL)
                if (!mOk) fallos++;
                sb.AppendLine((mOk ? "  ok    " : "  FALLO ") + "MirarDll del exe de la app: no vale como DLL");
            }
            catch (Exception ex) { fallos++; sb.AppendLine("FALLO excepcion: " + ex); }
            finally { try { if (Directory.Exists(raiz)) Directory.Delete(raiz, true); } catch { } }
            sb.AppendLine(fallos + " fallos");
            try { File.WriteAllText(string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_extras_guardados.txt") : informe, sb.ToString(), new UTF8Encoding(false)); } catch { }
            Console.Write(sb.ToString());
            return fallos;
        }
    }
}
