// Componentes.cs - mpv y Magpie, los programas libres de terceros que usa ConsoleEnhancer, NO van dentro del zip de la release: sus
// licencias (GPL de mpv y su FFmpeg; Magpie Experimental, que ademas lleva DLL de NVIDIA, AMD e Intel con sus propias condiciones y
// pide no volver a repartir sus binarios sin revisarlas) hacen que lo limpio sea que cada usuario los baje de su pagina oficial.
// La primera vez que se abre la app (antes del asistente) se bajan solos, con permiso, de sus releases oficiales de GitHub:
//  - la tabla es scripts\components.json (la misma de scripts\descargar_dependencias.ps1): version, URL, SHA-256 y que se coge del zip
//  - se comprueba el SHA-256 de cada zip; sin internet, valen los mismos zip puestos a mano en <app>\downloads\
//  - se sacan a <app>\mpv y <app>\magpie sin lo no oficial (nvngx_dlssnr.dll, runtimes\dlssg_sm86: los pone el usuario con el boton
//    "Anadir DLSS 5" de la bandeja o del asistente, src\Lanzador\Extras.cs; ver extras\LEEME.txt)
//  - bandeja > "Componentes: comprobar / volver a descargar" (Extras.cs): --componentes-reparar los vuelve a bajar todos
//  - <carpeta>\.version = "version sha256" (lo mismo que deja descargar_dependencias.ps1 en terceros\)
// Con la carpeta de la app sin permiso de escritura (Archivos de programa) lo explica y sale.
// Pruebas / scripts: ConsoleEnhancer.exe --componentes=<carpeta> (los baja o coge de <carpeta>\descargas, sin preguntar ni ventanas,
// informe en <carpeta>\componentes.log; 0 = bien). Ademas: "Acerca de y licencias" de la bandeja (AcercaDe, abajo).
// Vista previa de la ventana sin red ni consola: ConsoleEnhancer.exe --ui-preview=componentes;<carpeta> (una imagen PNG de cada
// estado -inicial, comprobando, descargando con %, sacando, error de huella, sin internet- en espanol e ingles; ver Componentes.Preview).
// (20-11-2026, agente GITHUB: preparar el repositorio publico; 20-11 agente CE-UI: la ventana ya no corta el texto y se ajusta al contenido)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    sealed class Componente
    {
        public string Nombre = "", Titulo = "", Version = "", Url = "", Sha256 = "", Archivo = "", Destino = "", Comprobar = "", QuitarPrefijo = "";
        public string DescripcionEs = "", DescripcionEn = "", Licencia = "", Web = "", Fuente = "";
        public long Bytes;
        public bool AlAbrir;
        public List<KeyValuePair<Regex, string>> Renombrar = new List<KeyValuePair<Regex, string>>();
        public List<Regex> Incluir = new List<Regex>(), Excluir = new List<Regex>(), NoOficial = new List<Regex>();

        public string Marca { get { return Version + " " + Sha256; } }
        public string Descripcion { get { return Tx.Ingles ? DescripcionEn : DescripcionEs; } }
    }

    static class Componentes
    {
        public static string Tabla { get { return Path.Combine(Paths.AppDir, @"scripts\components.json"); } }

        // ---- la tabla ----
        public static List<Componente> Leer(string file)
        {
            List<Componente> l = new List<Componente>();
            Dictionary<string, object> raiz = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file, Encoding.UTF8));
            foreach (object o in (object[])raiz["componentes"])
            {
                Dictionary<string, object> d = (Dictionary<string, object>)o;
                Componente c = new Componente();
                c.Nombre = S(d, "nombre"); c.Titulo = S(d, "titulo"); if (c.Titulo.Length == 0) c.Titulo = c.Nombre; c.Version = S(d, "version"); c.Url = S(d, "url"); c.Sha256 = S(d, "sha256").ToLowerInvariant();
                c.Archivo = S(d, "archivo"); c.Destino = S(d, "destino"); c.Comprobar = S(d, "comprobar"); c.QuitarPrefijo = S(d, "quitar_prefijo");
                c.DescripcionEs = S(d, "descripcion_es"); c.DescripcionEn = S(d, "descripcion_en"); c.Licencia = S(d, "licencia");
                c.Web = S(d, "web"); c.Fuente = S(d, "fuente");
                object v;
                if (d.TryGetValue("bytes", out v) && v != null) c.Bytes = Convert.ToInt64(v);
                c.AlAbrir = d.TryGetValue("al_abrir", out v) && v is bool && (bool)v;
                if (d.TryGetValue("renombrar", out v) && v is object[])
                    foreach (object r in (object[])v)
                    {
                        Dictionary<string, object> rr = (Dictionary<string, object>)r;
                        c.Renombrar.Add(new KeyValuePair<Regex, string>(new Regex(S(rr, "de"), RegexOptions.IgnoreCase), S(rr, "a")));
                    }
                Lista(d, "incluir", c.Incluir); Lista(d, "excluir", c.Excluir); Lista(d, "no_oficial", c.NoOficial);
                if (c.Nombre.Length == 0 || c.Url.Length == 0 || c.Sha256.Length != 64 || c.Destino.Length == 0 || c.Comprobar.Length == 0)
                    throw new FormatException("components.json: falta algo en " + (c.Nombre.Length > 0 ? c.Nombre : "un componente"));
                if (c.Destino.IndexOfAny(new char[] { '\\', '/', ':' }) >= 0 || c.Destino.Contains("..")) throw new FormatException("components.json: destino raro en " + c.Nombre);
                l.Add(c);
            }
            return l;
        }

        static string S(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : ""; }
        static void Lista(Dictionary<string, object> d, string k, List<Regex> l)
        {
            object v;
            if (d.TryGetValue(k, out v) && v is object[]) foreach (object o in (object[])v) if (o != null && Convert.ToString(o).Length > 0) l.Add(new Regex(Convert.ToString(o), RegexOptions.IgnoreCase));
        }

        // La ruta de dentro del zip ('/') que se queda, ya cambiada, o null. La misma regla que Ruta en scripts\descargar_dependencias.ps1
        public static string Ruta(Componente c, string r)
        {
            r = r.Replace('\\', '/');
            if (c.QuitarPrefijo.Length > 0) r = Regex.Replace(r, c.QuitarPrefijo, "", RegexOptions.IgnoreCase);
            foreach (KeyValuePair<Regex, string> x in c.Renombrar) if (x.Key.IsMatch(r)) r = x.Key.Replace(r, x.Value);
            if (r.Length == 0 || r.EndsWith("/")) return null;
            if (c.Incluir.Count > 0) { bool si = false; foreach (Regex x in c.Incluir) if (x.IsMatch(r)) { si = true; break; } if (!si) return null; }
            foreach (Regex x in c.Excluir) if (x.IsMatch(r)) return null;
            foreach (Regex x in c.NoOficial) if (x.IsMatch(r)) return null;
            return r;
        }

        // 0 = esta y es la version de la tabla (o lo ha puesto el usuario a mano, sin .version), 1 = esta pero de otra version, 2 = falta
        public static int Estado(Componente c, string dir)
        {
            string d = Path.Combine(dir, c.Destino);
            if (!File.Exists(Path.Combine(d, c.Comprobar.Replace('/', '\\')))) return 2;
            string marca = Path.Combine(d, ".version");
            try { if (File.Exists(marca) && File.ReadAllText(marca).Trim() != c.Marca) return 1; } catch { }
            return 0;
        }

        // (1.2.4 ronda 6, QUITAFG) Sin Magpie, las DLL de Intel XeSS y AMD FidelityFX que usa el motor van en xess\ y fsr\ (de sus releases
        // oficiales: componentes xess y fsr). Una carpeta de la app de antes las tiene en magpie\ (las mismas versiones; libxell 1.3.0.5 en vez
        // de 1.3.2.10): se copian ahi una vez y no hace falta bajarlas. Lo demas de magpie\ no se toca (es de la version anterior)
        static void RecogerDeMagpie()
        {
            try
            {
                string[][] grupos = { new[] { Paths.XessDir, "libxess.dll", "libxess_fg.dll", "libxell.dll", "INTEL-XESS-LICENSE.txt", "INTEL-XESS-THIRD-PARTY.txt" },
                                      new[] { Paths.FsrDir, "amd_fidelityfx_loader_dx12.dll", "amd_fidelityfx_upscaler_dx12.dll", "amd_fidelityfx_framegeneration_dx12.dll", "AMD-FSR-SDK-LICENSE.md" } };
                foreach (string[] g in grupos)
                {
                    if (File.Exists(Path.Combine(g[0], g[2]))) continue;
                    bool todas = true;
                    for (int i = 1; i <= 3; i++) if (!File.Exists(Path.Combine(Paths.MagpieDir, g[i]))) todas = false;
                    if (!todas) continue;
                    Directory.CreateDirectory(g[0]);
                    int n = 0;
                    for (int i = 1; i < g.Length; i++)
                    {
                        string de = Path.Combine(Paths.MagpieDir, g[i]);
                        if (!File.Exists(de)) continue;
                        File.Copy(de, Path.Combine(g[0], g[i]), true);
                        n++;
                    }
                    Log.Write("Componentes: " + n + " ficheros de " + Paths.MagpieDir + " (la instalacion de antes) copiados a " + g[0] + " para el motor (sin volver a bajarlos)");
                }
            }
            catch (Exception ex) { Log.Error("Componentes: recogiendo las DLL de magpie\\", ex); }
        }

        // ---- al abrir la app (Main, antes del asistente): -1 = seguir, otro = salir con ese codigo ----
        public static int Asegurar(string[] args, bool dryRun)
        {
            string prueba = null;
            bool sinPreguntar = dryRun, reparar = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--componentes=", StringComparison.OrdinalIgnoreCase)) prueba = a.Substring(14).Trim('"');
                else if (a == "--componentes-reparar") reparar = true;   // (20-11, PUBLICAR: bandeja > Componentes: volver a bajarlos)
                else if (a == "--asistente-auto" || a.StartsWith("--simula=") || a.StartsWith("--datos=") || a == "--inicio") sinPreguntar = true;
            }
            if (prueba != null) return Prueba(prueba);
            List<Componente> todos;
            try
            {
                if (!File.Exists(Tabla)) return -1;   // (bin\ de desarrollo sin la tabla: build.bat la copia)
                todos = Leer(Tabla);
            }
            catch (Exception ex) { Log.Error("Componentes: " + Tabla, ex); return -1; }
            if (!reparar) RecogerDeMagpie();   // (1.2.4 ronda 6, QUITAFG: las DLL de Intel / AMD de una instalacion de antes, sin volver a bajarlas)
            List<Componente> faltan = new List<Componente>();
            bool alguno = false;
            foreach (Componente c in todos)
            {
                if (!c.AlAbrir) continue;
                int e = Estado(c, Paths.AppDir);
                if (e == 2) alguno = true;
                if (e > 0 || reparar) faltan.Add(c);
            }
            if (faltan.Count == 0) return -1;
            List<string> n = new List<string>();
            foreach (Componente c in faltan) n.Add(c.Nombre + " " + c.Version + (Estado(c, Paths.AppDir) == 1 ? " (actualizar)" : " (falta)"));
            Log.Write("Componentes de terceros: " + string.Join(", ", n.ToArray()));
            if (sinPreguntar) return -1;
            try { Application.EnableVisualStyles(); } catch { }
            while (true)
                using (ComponentesForm f = new ComponentesForm(faltan, Paths.AppDir, !alguno, reparar && !alguno))
                {
                    f.ShowDialog();
                    if (f.CambioIdioma) continue;   // (1.2.06) otra vez, en el idioma elegido
                    if (f.Hecho) return -1;
                    // sin lo que falta del todo la app no puede ni probar la imagen: salir (la proxima vez se vuelve a ofrecer)
                    return alguno ? 0 : -1;
                }
        }

        static int Prueba(string dir)
        {
            StringBuilder sb = new StringBuilder();
            int r = 0;
            try
            {
                dir = Path.GetFullPath(dir).TrimEnd('\\');
                Directory.CreateDirectory(dir);
                foreach (Componente c in Leer(Tabla))
                {
                    if (!c.AlAbrir) continue;
                    Stopwatch sw = Stopwatch.StartNew();
                    long n = Instalar(c, dir, null, null);
                    sb.AppendLine(c.Nombre + " " + c.Version + ": " + n + " ficheros en " + Path.Combine(dir, c.Destino) + " (" + sw.ElapsedMilliseconds + " ms), estado " + Estado(c, dir));
                }
            }
            catch (Exception ex) { r = 1; sb.AppendLine("ERROR: " + ex.Message); }
            try { File.WriteAllText(Path.Combine(dir, "componentes.log"), sb.ToString(), new UTF8Encoding(false)); } catch { }
            return r;
        }

        public delegate void Progreso(string texto, long hecho, long total);

        // Baja (o coge de <dir>\descargas) y saca un componente a <dir>\<destino>. Devuelve los ficheros sacados
        public static long Instalar(Componente c, string dir, Progreso p, Func<bool> cancelado)
        {
            string cache = Path.Combine(dir, "downloads");
            Directory.CreateDirectory(cache);
            string zip = Path.Combine(cache, c.Archivo);
            // (1.2.02) sin internet el usuario baja el enlace de la ventana, que se guarda con el nombre de la URL (Magpie-Experimental-x64.zip),
            // no con c.Archivo: aceptar los dos
            if (!File.Exists(zip))
            {
                try
                {
                    string deUrl = Path.Combine(cache, Path.GetFileName(new Uri(c.Url).LocalPath));
                    if (File.Exists(deUrl)) zip = deUrl;
                }
                catch { }
            }
            bool bajado = false;
            if (File.Exists(zip))
            {
                if (p != null) p(Tx.T("Comprobando ", "Checking ") + c.Archivo + "...", 0, 0);
                if (Hash(zip) != c.Sha256) { Log.Write("Componentes: " + zip + " con otra huella SHA-256: se baja otra vez"); File.Delete(zip); }
            }
            if (!File.Exists(zip)) { Bajar(c, zip, p, cancelado); bajado = true; }
            long n = Sacar(c, zip, Path.Combine(dir, c.Destino), p, cancelado);
            if (bajado) { try { File.Delete(zip); } catch { } }
            try { if (Directory.GetFileSystemEntries(cache).Length == 0) Directory.Delete(cache); } catch { }
            Log.Write("Componentes: " + c.Nombre + " " + c.Version + " instalado en " + Path.Combine(dir, c.Destino) + " (" + n + " ficheros, de " + (bajado ? c.Url : zip) + ")");
            return n;
        }

        static string Hash(string file)
        {
            using (SHA256 h = SHA256.Create())
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                return Hex(h.ComputeHash(fs));
        }

        static string Hex(byte[] b) { StringBuilder sb = new StringBuilder(b.Length * 2); foreach (byte x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }

        static void Bajar(Componente c, string zip, Progreso p, Func<bool> cancelado)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
            string tmp = zip + ".parcial";
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(c.Url);
            rq.UserAgent = "ConsoleEnhancer";
            rq.AllowAutoRedirect = true;
            rq.Timeout = 30000;
            rq.ReadWriteTimeout = 60000;
            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
            using (Stream s = rs.GetResponseStream())
            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (SHA256 h = SHA256.Create())
            {
                long total = rs.ContentLength > 0 ? rs.ContentLength : c.Bytes, hecho = 0;
                byte[] buf = new byte[1 << 16];
                Stopwatch sw = Stopwatch.StartNew();
                long ultimo = -1000;
                int k;
                while ((k = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelado != null && cancelado()) throw new OperationCanceledException();
                    fs.Write(buf, 0, k);
                    h.TransformBlock(buf, 0, k, null, 0);
                    hecho += k;
                    if (p != null && sw.ElapsedMilliseconds - ultimo >= 200)
                    {
                        ultimo = sw.ElapsedMilliseconds;
                        double mbs = hecho / 1048576.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                        p(Tx.T("Bajando ", "Downloading ") + c.Titulo + " " + c.Version + ": " + (hecho >> 20) + " / " + (total >> 20) + " MB (" +
                          mbs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB/s)", hecho, total);
                    }
                }
                h.TransformFinalBlock(buf, 0, 0);
                string got = Hex(h.Hash);
                fs.Close();
                if (got != c.Sha256)
                {
                    try { File.Delete(tmp); } catch { }
                    throw new InvalidDataException(Tx.T("La descarga de ", "The download of ") + c.Nombre + Tx.T(" no tiene la huella SHA-256 esperada (", " does not have the expected SHA-256 (") + got + ")");
                }
            }
            if (File.Exists(zip)) File.Delete(zip);
            File.Move(tmp, zip);
        }

        // Saca a <destino>.parcial y luego pone cada fichero en su sitio: un corte a medias no deja un Magpie.exe sin el resto
        static long Sacar(Componente c, string zip, string destino, Progreso p, Func<bool> cancelado)
        {
            string tmp = destino + ".parcial";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            string full = Path.GetFullPath(tmp).TrimEnd('\\') + "\\";
            List<string> rels = new List<string>();
            using (ZipArchive z = ZipFile.OpenRead(zip))
            {
                int i = 0;
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    i++;
                    if (cancelado != null && cancelado()) throw new OperationCanceledException();
                    string rel = Ruta(c, e.FullName);
                    if (rel == null) continue;
                    string to = Path.GetFullPath(Path.Combine(tmp, rel.Replace('/', '\\')));
                    if (!to.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ruta rara en el zip: " + e.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    e.ExtractToFile(to, true);
                    rels.Add(rel.Replace('/', '\\'));
                    if (p != null && (i & 15) == 0) p(Tx.T("Sacando ", "Extracting ") + c.Titulo + ": " + rels.Count + Tx.T(" ficheros", " files"), i, z.Entries.Count);
                }
            }
            if (!File.Exists(Path.Combine(tmp, c.Comprobar.Replace('/', '\\')))) throw new InvalidDataException(c.Nombre + ": " + c.Comprobar + Tx.T(" no esta en el zip", " is not in the zip"));
            foreach (string rel in rels)
            {
                string to = Path.Combine(destino, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (File.Exists(to)) File.Delete(to);
                File.Move(Path.Combine(tmp, rel), to);
            }
            Directory.Delete(tmp, true);
            File.WriteAllText(Path.Combine(destino, ".version"), c.Marca + "\r\n", Encoding.ASCII);
            return rels.Count;
        }

        // (1.2.07-1.2.4: AsegurarSm86 y lo demas de dlssg_sm86, el runtime de la comunidad que pedia el DLSS FG de Magpie. 1.2.4 ronda 6,
        // QUITAFG: sin DLSS FG en la app, fuera)

        public static bool SePuedeEscribir(string dir)
        {
            try { string f = Path.Combine(dir, ".escribir_componentes"); File.WriteAllText(f, ""); File.Delete(f); return true; }
            catch { return false; }
        }

        public static long Libre(string dir)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))).AvailableFreeSpace; } catch { return long.MaxValue; }
        }

        // ---- vista previa sin red ni consola: --ui-preview=componentes;<carpeta> ----
        // (20-11, CE-UI) Dibuja la ventana del primer arranque en sus estados (inicial, comprobando, descargando con %, sacando,
        // error de huella y sin internet) en espanol e ingles y los guarda como PNG, para revisar que no se corta ningun texto sin
        // tener que bajar nada. Usa datos de ejemplo: no lee components.json ni escribe en la carpeta de la app.
        public static int Preview(string carpeta)
        {
            Directory.CreateDirectory(carpeta);
            try { Application.EnableVisualStyles(); } catch { }
            List<Componente> faltan = Ejemplo();
            // una ruta larga a proposito, para comprobar que la etiqueta no la corta
            string dir = @"D:\Juegos\Consolas\ConsoleEnhancer (portable, carpeta de ejemplo bien larga)";
            string[] estados = { "inicial", "comprobando", "descargando", "sacando", "error", "sin-internet" };
            foreach (string lang in new string[] { "es", "en" })
            {
                Tx.Ingles = lang == "en";
                try { Idioma.Poner(lang); } catch { }
                foreach (string e in estados)
                {
                    using (ComponentesForm f = new ComponentesForm(faltan, dir, false))
                    {
                        f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, -4000); f.ShowInTaskbar = false;
                        f.Show();
                        for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(15); }
                        f.PreviewEstado(e);
                        for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(15); }
                        using (Bitmap b = new Bitmap(f.Width, f.Height))
                        {
                            f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                            b.Save(Path.Combine(carpeta, "componentes_" + lang + "_" + e + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        f.Close();
                    }
                }
            }
            Log.Write("Componentes: vista previa en " + carpeta);
            return 0;
        }

        // Dos componentes de ejemplo para la vista previa (no se baja nada; los textos y tamanos son realistas)
        static List<Componente> Ejemplo()
        {
            List<Componente> l = new List<Componente>();
            Componente mpv = new Componente();
            mpv.Nombre = "mpv"; mpv.Titulo = "mpv"; mpv.Version = "0.39.0";
            mpv.DescripcionEs = "reproductor que enseña la imagen de la capturadora con poca latencia";
            mpv.DescripcionEn = "player that shows the capture picture with low latency";
            mpv.Licencia = "GPL-2.0-or-later"; mpv.Bytes = 45000000L;
            mpv.Url = "https://github.com/mpv-player/mpv/releases/download/v0.39.0/mpv-x86_64-20250101.zip";
            l.Add(mpv);
            Componente mag = new Componente();
            mag.Nombre = "magpie"; mag.Titulo = "Magpie"; mag.Version = "0.11.0-pre";
            mag.DescripcionEs = "reescalado de la imagen y generación de fotogramas (FSR, NIS y la generación de Magpie)";
            mag.DescripcionEn = "image upscaling and frame generation (FSR, NIS and Magpie's own frame generation)";
            mag.Licencia = "GPL-3.0-only"; mag.Bytes = 620000000L;
            mag.Url = "https://github.com/Blinue/Magpie/releases/download/v0.11.0-pre/Magpie-v0.11.0-pre-x64.zip";
            l.Add(mag);
            return l;
        }
    }

    // La ventana de la primera vez: que falta, de donde sale, cuanto ocupa; [Descargar] / [Salir].
    // (20-11, CE-UI) La ventana se ajusta al contenido y TODAS las etiquetas crecen a lo alto solas (AutoSize + ancho maximo con
    // salto de linea), para que ni el texto largo del ingles, ni las rutas largas, ni los mensajes de error se corten. Antes tenian
    // alto fijo (230 / 40 px) y recortaban el texto. Aspecto oscuro, igual que el asistente del primer arranque (Prisma), para que
    // las dos ventanas combinen. El escalado por DPI lo hace Windows (la app no declara conciencia de DPI), asi que aqui k vale 1 y
    // el texto fluido evita recortes aunque Windows estire la ventana en una tele 4K.
    sealed class ComponentesForm : Form
    {
        // mismos colores que el asistente (Asistente.cs), para que las dos ventanas del primer arranque combinen
        static readonly Color Fondo = Color.FromArgb(18, 20, 28), Panel_ = Color.FromArgb(30, 33, 45),
                              TextoCol = Color.FromArgb(230, 234, 242), Suave = Color.FromArgb(150, 158, 175), Acento = Color.FromArgb(0, 190, 240);

        readonly List<Componente> faltan;
        readonly string dir;
        readonly Label estado;
        readonly ProgressBar barra;
        readonly Button bajar, salir;
        volatile bool cancelar;
        Thread hilo;
        public bool Hecho;
        public bool CambioIdioma;   // (1.2.06) cerrada para volver a abrirla en el otro idioma

        // DPI a mano por si algun dia la app declara conciencia de DPI; hoy da 1 (Windows escala la ventana sola)
        static float Escala() { try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); } catch { return 1f; } }
        readonly float k = Escala();
        int S(int v) { return (int)Math.Round(v * k); }

        public ComponentesForm(List<Componente> faltan, string dir, bool soloActualizar, bool reparar = false)
        {
            this.faltan = faltan;
            this.dir = dir;
            Text = "ConsoleEnhancer";
            try { Icon own = PrismaUi.AppIcon(32); Icon = own != null ? own : Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BackColor = Fondo; ForeColor = TextoCol;
            Font = PrismaUi.BodyFontPt(10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None;   // el reparto se hace a mano; nada de doble escalado
            StartPosition = FormStartPosition.CenterScreen;

            const int M = 24;     // margen
            const int W = 600;    // ancho del contenido (logico)
            const int gap = 12;   // aire entre bloques
            int y = S(M);

            // ---- cabecera: logotipo + nombre (como en la bandeja / el asistente) ----
            int logo = S(30);
            PictureBox pb = new PictureBox();
            pb.SetBounds(S(M), y, logo, logo); pb.SizeMode = PictureBoxSizeMode.Zoom; pb.BackColor = Color.Transparent;
            try { Icon ico = PrismaUi.AppIcon(logo); if (ico != null) { pb.Image = ico.ToBitmap(); ico.Dispose(); } } catch { }
            Controls.Add(pb);
            Label cab = new Label();
            cab.AutoSize = true; cab.UseMnemonic = false; cab.Font = PrismaUi.TitleFontPt(14f); cab.ForeColor = TextoCol;
            cab.Text = PrismaUi.ShownName;
            cab.Location = new Point(S(M) + logo + S(10), y + (logo - cab.PreferredSize.Height) / 2);
            Controls.Add(cab);
            // (1.2.06, el usuario: "al abrir la app [en ingles] y ahi pueda elegir el idioma"): English / Español arriba a la derecha;
            // al cambiarlo la ventana se vuelve a abrir en ese idioma (Componentes.Asegurar) y queda elegido (Idioma.Elegir)
            int xi = S(M) + S(W);
            foreach (string[] li in new[] { new[] { "es", "Español" }, new[] { "en", "English" } })
            {
                string lang = li[0];
                Button bi = BotonCe(li[1]);
                bi.Width = Math.Max(S(84), TextRenderer.MeasureText(li[1], Font).Width + S(20)); bi.Height = S(28);
                xi -= bi.Width;
                bi.Location = new Point(xi, y + (logo - bi.Height) / 2);
                xi -= S(8);
                if (Tx.Ingles == (lang == "en")) { bi.BackColor = Acento; bi.ForeColor = Fondo; }
                bi.TabStop = false;
                bi.Click += delegate
                {
                    if (hilo != null && hilo.IsAlive) return;   // (bajando: no)
                    if (Tx.Ingles == (lang == "en")) return;
                    Idioma.Elegir(lang);
                    CambioIdioma = true;
                    Close();
                };
                Controls.Add(bi);
            }
            y += logo + S(gap);

            long total = 0;
            StringBuilder lista = new StringBuilder(), urls = new StringBuilder();
            foreach (Componente c in faltan)
            {
                total += c.Bytes;
                lista.Append("  •  ").Append(c.Titulo).Append(" ").Append(c.Version).Append(" - ").Append(c.Descripcion).Append(" (").Append(c.Licencia.Split('(')[0].Trim())
                     .Append(", ").Append(Mb(c.Bytes)).Append(" MB)\r\n");
                urls.Append(c.Url).Append("\r\n");
            }
            // ---- texto principal: intro + la lista + de donde sale y cuanto ocupa (una etiqueta que crece sola, no se corta) ----
            string cuerpo = (reparar
                ? Tx.T("Se vuelven a bajar estos programas de sus páginas oficiales (para reparar una instalación dañada):\r\n\r\n",
                       "These programs are downloaded again from their official pages (to repair a damaged install):\r\n\r\n")
                : soloActualizar
                ? Tx.T("Esta versión de ConsoleEnhancer va con versiones más nuevas de estos programas:\r\n\r\n",
                       "This version of ConsoleEnhancer works with newer versions of these programs:\r\n\r\n")
                : Tx.T("Falta preparar dos programas libres de terceros que usa ConsoleEnhancer. No van dentro de su zip: cada uno se baja de " +
                       "su página oficial, con su licencia y su código fuente. Se bajan ahora, una sola vez:\r\n\r\n",
                       "ConsoleEnhancer uses two free third-party programs that are not inside its zip: each one is downloaded from its " +
                       "official page, with its own license and source code. They are downloaded now, only once:\r\n\r\n")) +
                lista.ToString() + "\r\n" +
                Tx.T("Se bajan de sus releases oficiales de GitHub, se comprueba su huella SHA-256 y se guardan en esta carpeta:\r\n" + dir + "\r\n" +
                     "No se baja ninguna DLL no oficial. Unos " + Mb(total) + " MB; luego ocupan aprox. 1 GB.",
                     "They come from their official GitHub releases, their SHA-256 is checked and they are saved in this folder:\r\n" + dir + "\r\n" +
                     "No unofficial DLL is downloaded. About " + Mb(total) + " MB; about 1 GB once installed.");
            Parrafo(cuerpo, false, S(M), S(W), ref y); y += S(gap);

            // ---- sin internet: poner los zip a mano ----
            try { Directory.CreateDirectory(Path.Combine(dir, "downloads")); } catch { }   // (1.2.02: que la carpeta exista ya)
            Parrafo(Tx.T("¿Sin internet en este PC? Baja estos ficheros en otro y ponlos en la carpeta \"downloads\" de la app:",
                         "No internet on this PC? Download these files elsewhere and put them in the app's \"downloads\" folder:"), true, S(M), S(W), ref y);
            y += S(4);
            TextBox enlaces = new TextBox();
            enlaces.Multiline = true; enlaces.ReadOnly = true; enlaces.WordWrap = false; enlaces.ScrollBars = ScrollBars.Horizontal;
            enlaces.BorderStyle = BorderStyle.FixedSingle; enlaces.BackColor = Color.FromArgb(12, 13, 18); enlaces.ForeColor = Suave;
            enlaces.Font = new Font("Consolas", 8.5f);
            enlaces.Text = urls.ToString().TrimEnd();
            enlaces.TabStop = false;
            int lineasUrl = Math.Max(2, faltan.Count);
            // alto para todas las lineas MAS la barra de scroll horizontal (si no, la ultima url queda tapada por la barra)
            enlaces.SetBounds(S(M), y, S(W), enlaces.Font.Height * lineasUrl + SystemInformation.HorizontalScrollBarHeight + S(8));
            Controls.Add(enlaces);
            y += enlaces.Height + S(gap);

            // ---- barra de progreso + estado (el estado crece a lo alto para no cortar ni los errores largos) ----
            barra = new ProgressBar();
            barra.SetBounds(S(M), y, S(W), S(16));
            barra.Maximum = 1000;
            Controls.Add(barra);
            y += barra.Height + S(8);
            estado = new Label();
            estado.AutoSize = false; estado.UseMnemonic = false; estado.ForeColor = TextoCol;
            estado.SetBounds(S(M), y, S(W), estado.Font.Height * 3 + S(6));   // sitio para 3 lineas (SHA-256, "sin internet", etc.)
            Controls.Add(estado);
            y += estado.Height + S(gap);

            // ---- botones, abajo a la derecha, con ancho segun su texto (no se solapan ni se salen) ----
            bajar = BotonCe(reparar ? Tx.T("Volver a bajar", "Download again") : soloActualizar ? Tx.T("Actualizar", "Update") : Tx.T("Descargar", "Download"));
            salir = BotonCe(soloActualizar ? Tx.T("Ahora no", "Not now") : Tx.T("Salir", "Exit"));
            int bH = S(34), right = S(M) + S(W);
            salir.SetBounds(right - salir.Width, y, salir.Width, bH);
            bajar.SetBounds(salir.Left - S(10) - bajar.Width, y, bajar.Width, bH);
            bajar.Click += delegate { Empezar(); };
            salir.Click += delegate { if (hilo != null && hilo.IsAlive) { cancelar = true; salir.Enabled = false; } else Close(); };
            Controls.Add(bajar); Controls.Add(salir);
            AcceptButton = bajar;

            ClientSize = new Size(S(M) + S(W) + S(M), y + bH + S(M));
            MinimumSize = Size;   // ya esta ajustada al contenido; no encoger
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (hilo != null && hilo.IsAlive) { cancelar = true; e.Cancel = true; } };
        }

        // Una etiqueta que ocupa lo que haga falta a lo alto (ancho fijo, salto de linea): nunca corta el texto
        void Parrafo(string t, bool suave, int x, int wpx, ref int y)
        {
            Label l = new Label();
            l.AutoSize = true; l.UseMnemonic = false;
            l.MaximumSize = new Size(wpx, 0);
            l.ForeColor = suave ? Suave : TextoCol;
            l.Text = t;
            l.Location = new Point(x, y);
            Controls.Add(l);
            y += l.PreferredSize.Height;
        }

        // Boton plano oscuro con borde de acento, ancho segun su texto (minimo sensato)
        Button BotonCe(string t)
        {
            Button b = new Button();
            b.Text = t; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Acento; b.BackColor = Panel_; b.ForeColor = TextoCol;
            int w = S(118);
            try { w = Math.Max(w, TextRenderer.MeasureText(t, Font).Width + S(28)); } catch { }
            b.Width = w; b.Height = S(34);
            return b;
        }

        static long Mb(long bytes) { return (bytes + 500000) / 1000000; }   // (MB de 10^6, como en el README)

        // (20-11, CE-UI) pone la ventana en un estado concreto para la vista previa (Componentes.Preview)
        internal void PreviewEstado(string tipo)
        {
            switch (tipo)
            {
                case "comprobando":
                    bajar.Enabled = false; salir.Text = Tx.T("Cancelar", "Cancel");
                    estado.Text = Tx.T("Comprobando ", "Checking ") + "Magpie-v0.11.0-pre-x64.zip...";
                    barra.Value = 0; break;
                case "descargando":
                    bajar.Enabled = false; salir.Text = Tx.T("Cancelar", "Cancel");
                    estado.Text = Tx.T("Bajando ", "Downloading ") + "Magpie 0.11.0-pre: 214 / 620 MB (18.6 MB/s)";
                    barra.Value = 345; break;
                case "sacando":
                    bajar.Enabled = false; salir.Text = Tx.T("Cancelar", "Cancel");
                    estado.Text = Tx.T("Sacando ", "Extracting ") + "Magpie: 1234" + Tx.T(" ficheros", " files");
                    barra.Value = 1000; break;
                case "error":
                    bajar.Enabled = true; bajar.Text = Tx.T("Reintentar", "Retry"); salir.Text = Tx.T("Salir", "Exit");
                    estado.Text = Tx.T("La descarga de ", "The download of ") + "Magpie" +
                                  Tx.T(" no tiene la huella SHA-256 esperada (", " does not have the expected SHA-256 (") +
                                  "9f2c8b17a4e03d5c6f1289ab)"; break;
                case "sin-internet":
                    bajar.Enabled = true; bajar.Text = Tx.T("Reintentar", "Retry"); salir.Text = Tx.T("Salir", "Exit");
                    estado.Text = Tx.T("No se pudo bajar (¿sin internet?): ", "Download failed (no internet?): ") +
                                  Tx.T("No se puede resolver el nombre del servidor remoto 'github.com'.", "The remote name could not be resolved: 'github.com'.");
                    barra.Value = 0; break;
            }
        }

        void Poner(string t, long hecho, long total)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    estado.Text = t;
                    if (total > 0) barra.Value = (int)Math.Max(0, Math.Min(1000, hecho * 1000 / total));
                });
            }
            catch { }
        }

        void Empezar()
        {
            if (!Componentes.SePuedeEscribir(dir))
            {
                MessageBox.Show(this, Tx.T("No se puede escribir en " + dir + ".\r\n\r\nDescomprime ConsoleEnhancer en una carpeta tuya (por ejemplo Documentos, " +
                                           "el Escritorio o D:\\Juegos), no en Archivos de programa, y ábrelo desde ahí.",
                                           "Cannot write to " + dir + ".\r\n\r\nUnzip ConsoleEnhancer into one of your own folders (Documents, Desktop, D:\\Games...), " +
                                           "not Program Files, and open it from there."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            long falta = 0;
            foreach (Componente c in faltan) falta += c.Bytes * 3;
            if (Componentes.Libre(dir) < falta)
            {
                MessageBox.Show(this, Tx.T("No hay sitio en el disco: hacen falta unos " + (falta >> 30) + " GB libres.", "Not enough disk space: about " + (falta >> 30) + " GB free are needed."),
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bajar.Enabled = false;
            salir.Text = Tx.T("Cancelar", "Cancel");
            cancelar = false;
            hilo = new Thread(delegate ()
            {
                string error = null;
                try
                {
                    foreach (Componente c in faltan)
                        Componentes.Instalar(c, dir, Poner, delegate { return cancelar; });
                }
                catch (OperationCanceledException) { error = Tx.T("Cancelado.", "Cancelled."); }
                catch (Exception ex)
                {
                    Log.Error("Componentes", ex);
                    error = (ex is WebException ? Tx.T("No se pudo bajar (¿sin internet?): ", "Download failed (no internet?): ") : "") + ex.Message;
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (error == null) { Hecho = true; Close(); return; }
                        estado.Text = error;
                        barra.Value = 0;
                        bajar.Enabled = true; bajar.Text = Tx.T("Reintentar", "Retry");
                        salir.Enabled = true; salir.Text = Tx.T("Salir", "Exit");
                    });
                }
                catch { }
            });
            hilo.IsBackground = true;
            hilo.Start();
        }
    }

    // "Acerca de y licencias" (menu de la bandeja): la licencia, sin garantia, de donde sale cada pieza y los avisos de NVIDIA / AMD
    // (la licencia de los SDK de NVIDIA RTX pide nombrar DLSS en el "acerca de"; la GPL-3.0, enseñar los avisos legales)
    static class AcercaDe
    {
        public static string Texto()
        {
            string v = "";
            try { Version x = Assembly.GetExecutingAssembly().GetName().Version; if (x != null && x.Major + x.Minor + x.Build > 0) v = " " + x.ToString(3); } catch { }
            return "ConsoleEnhancer" + v + "\r\n" +
                   Tx.T("Copyright (C) 2026 IanRog29 y colaboradores de ConsoleEnhancer", "Copyright (C) 2026 IanRog29 and ConsoleEnhancer contributors") + "\r\n" +
                   Apoyo.Repo + "\r\n\r\n" +
                   Tx.T("Software libre: puedes repartirlo y cambiarlo según la GNU GPL versión 3 (con un permiso adicional para usarlo con las " +
                        "librerías de NVIDIA, AMD e Intel: LICENSE-EXCEPTION.txt). Va SIN NINGUNA GARANTÍA. Licencias y componentes de terceros: " +
                        "carpeta licenses\\ (README.txt).",
                        "Free software: you can redistribute and modify it under the GNU GPL version 3 (with an additional permission to use it " +
                        "with the NVIDIA, AMD and Intel libraries: LICENSE-EXCEPTION.txt). It comes WITH ABSOLUTELY NO WARRANTY. Licenses and " +
                        "third-party components: the licenses\\ folder (README.txt).") + "\r\n\r\n" +
                   Tx.T("Usa: NVIDIA DLSS, NVIDIA DLSS Frame Generation, NVIDIA Reflex y NVIDIA Streamline (© NVIDIA Corporation, con sus licencias); " +
                        "AMD FidelityFX Super Resolution 1 y CAS (© Advanced Micro Devices, Inc., MIT); NVIDIA FXAA 3.11 (© NVIDIA Corporation, BSD); " +
                        "mpv y Magpie Experimental (GPL, bajados de sus páginas oficiales).",
                        "Uses: NVIDIA DLSS, NVIDIA DLSS Frame Generation, NVIDIA Reflex and NVIDIA Streamline (© NVIDIA Corporation, under their " +
                        "licenses); AMD FidelityFX Super Resolution 1 and CAS (© Advanced Micro Devices, Inc., MIT); NVIDIA FXAA 3.11 (© NVIDIA " +
                        "Corporation, BSD); mpv and Magpie Experimental (GPL, downloaded from their official pages).") + "\r\n\r\n" +
                   Tx.T("NVIDIA, DLSS, Reflex, GeForce RTX, AMD, FidelityFX, Intel, XeSS, Nintendo, PlayStation, Xbox y las demás marcas son de " +
                        "sus dueños. ConsoleEnhancer no está afiliado a ninguno de ellos.",
                        "NVIDIA, DLSS, Reflex, GeForce RTX, AMD, FidelityFX, Intel, XeSS, Nintendo, PlayStation, Xbox and other marks belong to " +
                        "their owners. ConsoleEnhancer is not affiliated with any of them.") + "\r\n\r\n" +
                   Apoyo.Linea;   // (20-11, PUBLICAR: una linea, voluntario; bandeja > Apoyar el proyecto)
        }

        public static void Mostrar()
        {
            string lic = Path.Combine(Paths.AppDir, "licenses");
            DialogResult r = MessageBox.Show(Texto() + "\r\n\r\n" + Tx.T("¿Abrir la carpeta de licencias?", "Open the licenses folder?"),
                                             Tx.T("Acerca de ConsoleEnhancer", "About ConsoleEnhancer"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r != DialogResult.Yes) return;
            try { Process.Start("explorer.exe", "\"" + (Directory.Exists(lic) ? lic : Paths.AppDir) + "\""); }
            catch (Exception ex) { Log.Error("Abriendo las licencias", ex); }
        }
    }

    partial class TrayApp
    {
        // (una linea en el menu de la bandeja, junto a la de MenuCe)
        void MenuAcercaDe(ContextMenuStrip menu)
        {
            menu.Items.Add(Tx.T("Acerca de y licencias", "About and licenses"), null, delegate { AcercaDe.Mostrar(); });
        }
    }
}
