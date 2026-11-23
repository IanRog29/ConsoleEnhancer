// Capturadoras.cs - capturadoras de video (DirectShow y Media Foundation), su audio y sus formatos (ConsoleEnhancer)
//
// SIN ABRIR NADA (siempre):
//  - DirectShow: los dispositivos de video y de audio (nombre, DevicePath, CLSID). Se lee su "property bag" (registro), no se crea el filtro.
//  - Media Foundation (alternativa): la lista de MFEnumDeviceSources (nombre y enlace simbolico); tampoco abre (no se llama a ActivateObject).
//  - Configuration Manager: de cada uno, su nodo, padres, bus (PCI/USB), VID/PID y contenedor.
//  - Audio de Windows (WASAPI) por el registro (MMDevices\Audio\Capture): entradas activas y su nodo.
//  - Catalogo de modelos conocidos (AVerMedia, Elgato, Magewell, MacroSilicon MS2109/MS2130...) y clasificacion: capturadora / webcam / virtual.
//  - Emparejar el audio: el mismo nombre en DirectShow (AVerMedia: el audio va en el mismo filtro), el mismo aparato USB (padre comun),
//    el mismo contenedor, o el nombre parecido.
// ABRIENDO EL DISPOSITIVO (solo si se pide: OpcionesDeteccion.AbrirCapturadoras y el permiso AntesDeAbrir):
//  - Formatos del pin de video (IAMStreamConfig::GetStreamCaps): resolucion, fps minimos/maximos, subtipo. Y si el filtro tiene pines de audio.
//    No se cambia el formato ni se pone en marcha nada.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace ConsoleEnhancer.Deteccion
{
    public enum TipoCaptura { Desconocido, Capturadora, Webcam, Virtual }

    public sealed class FormatoCaptura
    {
        public int Ancho, Alto;
        public double FpsMax, FpsMin;
        public string Subtipo = "";
        public bool Comprimido;
        public bool Previsto;             // (1.2.2) sacado del catalogo, no de la capturadora abierta
        public override string ToString()
        {
            return Ancho + "x" + Alto + " " + Subtipo + " " + Tx.N(FpsMax, "0.##") + (FpsMin > 0 && Math.Abs(FpsMin - FpsMax) > 0.5 ? " (" + Tx.N(FpsMin, "0.##") + "-" + Tx.N(FpsMax, "0.##") + ")" : "") + " fps";
        }
    }

    public sealed class AudioEntrada
    {
        public string Nombre = "";
        public string Origen = "";        // dshow / wasapi
        public string Id = "";            // DevicePath (dshow) o id del endpoint (wasapi)
        public string Clsid = "";         // dshow: el filtro (el de audio de Windows o el del fabricante)
        public string Instancia = "";
        public List<string> Cadena = new List<string>();
        public Guid Contenedor;
        public bool Activa = true;
    }

    public sealed class ModeloCaptura
    {
        public string Marca, Modelo, Bus, Formatos, Notas;
        public int Vid, Pid;              // 0 = cualquiera (por nombre)
        public string[] Patrones;
        public int MaxW, MaxH; public double MaxFps;          // lo mejor sin comprimir, segun el fabricante
        public bool SoloComprimido;       // MS2109: a 1080p solo MJPEG
        public bool HdmiSinSenalPropia;   // pinta su propia pantalla "No Signal" (AVerMedia)
        public bool AudioEnFiltro;        // el audio sale por pines del propio filtro de video (GC573: mpv "dshow:audio=<nombre>" + audio_pin_name)
        // 22-11 (1.2.2, agente UNIVERSAL; el usuario: "aprendete como son todas las elgato, avermedia, etc"): lo que se sabe de cada modelo
        // como DATOS (la tabla Catalogo), no como codigo por modelo. Ver CatalogoCapturas.cs para la sintaxis y las fuentes.
        public string Driver = "";        // "uvc" (el de Windows, sin instalar nada) o el del fabricante ("propio: <nombre>")
        public string Modos = "";         // lo que da SIN abrirla (formatos previstos): "2160p60:NV12/YUY2 1440p120:NV12 1080p60:MJPG"
        public string Malos = "";         // lo que lista y no da: "2160:RGB24=53" (a 4K en RGB24 lista 60 y llega a 53)
        public int LatenciaMs;            // latencia de su vista previa (fabricante o medida; 0 = no se sabe)
        public bool Hdr;                  // captura HDR10 (P010) ademas de dejarlo pasar
        public string Paso = "";          // lo que deja pasar a la tele (passthrough), informativo
        public string Rarezas = "";       // lo que conviene saber (va al informe y al asistente)
        public string Clave = "";         // nombre corto (simulaciones: DeteccionTest --simula=cap:<clave>)
        public ModeloCaptura Id(string clave) { Clave = clave ?? ""; return this; }
        public ModeloCaptura(string marca, string modelo, int vid, int pid, string bus, string[] pat, int w, int h, double fps, bool soloComp, string formatos, string notas)
        { Marca = marca; Modelo = modelo; Vid = vid; Pid = pid; Bus = bus; Patrones = pat; MaxW = w; MaxH = h; MaxFps = fps; SoloComprimido = soloComp; Formatos = formatos; Notas = notas; }
        public ModeloCaptura Con(bool audioEnFiltro, bool noSignalPropia) { AudioEnFiltro = audioEnFiltro; HdmiSinSenalPropia = noSignalPropia; return this; }
        // (1.2.2) los datos de la tabla: driver, modos previstos, modos malos, latencia (ms), HDR, passthrough y rarezas
        public ModeloCaptura Datos(string driver, string modos, string malos, int latMs, bool hdr, string paso, string rarezas)
        { Driver = driver ?? ""; Modos = modos ?? ""; Malos = malos ?? ""; LatenciaMs = latMs; Hdr = hdr; Paso = paso ?? ""; Rarezas = rarezas ?? ""; return this; }

        // Los formatos previstos (Modos) como si se hubiera abierto: para elegir sin abrirla (el asistente los confirma al abrirla)
        public List<FormatoCaptura> Previstos()
        {
            List<FormatoCaptura> l = new List<FormatoCaptura>();
            foreach (string tok in Modos.Split(new char[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int p = tok.IndexOf('p'), dp = tok.IndexOf(':');
                if (p <= 0 || dp <= p) continue;
                int alto; double fps;
                if (!int.TryParse(tok.Substring(0, p), NumberStyles.Integer, CultureInfo.InvariantCulture, out alto) ||
                    !double.TryParse(tok.Substring(p + 1, dp - p - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out fps)) continue;
                foreach (string sub in tok.Substring(dp + 1).Split('/'))
                {
                    if (sub.Length == 0) continue;
                    FormatoCaptura f = new FormatoCaptura();
                    f.Alto = alto; f.Ancho = AnchoDe(alto); f.FpsMax = fps; f.FpsMin = Math.Min(fps, 24);
                    f.Subtipo = sub.ToUpperInvariant(); f.Comprimido = EsComprimido(f.Subtipo);
                    f.Previsto = true;
                    l.Add(f);
                }
            }
            return l;
        }

        // Las imagenes por segundo de verdad de un formato que la tarjeta lista con 'listado' (Malos: "2160:RGB24=53")
        public double FpsReal(int alto, string sub, double listado)
        {
            if (Malos.Length == 0) return listado;
            foreach (string tok in Malos.Split(new char[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int dp = tok.IndexOf(':'), eq = tok.IndexOf('=');
                if (dp <= 0 || eq <= dp) continue;
                int a; double f;
                string alt = tok.Substring(0, dp);
                // ("*" = a cualquier alto: las PCIe de Elgato dan RGB24 / RGB32 que ffmpeg no sabe leer -"unknown compression"-)
                if (alt != "*" && (!int.TryParse(alt, NumberStyles.Integer, CultureInfo.InvariantCulture, out a) || a != alto)) continue;
                if (!string.Equals(tok.Substring(dp + 1, eq - dp - 1), sub, StringComparison.OrdinalIgnoreCase)) continue;
                if (double.TryParse(tok.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return Math.Min(listado, f);
            }
            return listado;
        }

        public static int AnchoDe(int alto) { return alto >= 2160 ? 3840 : alto >= 1440 ? 2560 : alto >= 1080 ? 1920 : alto >= 720 ? 1280 : alto >= 576 ? 720 : 640; }
        public static bool EsComprimido(string sub) { return sub == "MJPG" || sub == "H264" || sub == "HEVC" || sub == "H265" || sub == "MPEG2"; }
    }

    public sealed class CapturadoraInfo
    {
        public string Nombre = "";
        public string DevicePath = "";
        public string Clsid = "";
        public string Instancia = "";
        public List<string> Cadena = new List<string>();
        public string Bus = "";           // PCI, USB, SWD (software)...
        public int Vid, Pid;
        public string NombrePnp = "", Fabricante = "", Servicio = "";
        public Guid Contenedor;
        public bool EnMediaFoundation, EnDirectShow = true;
        public ModeloCaptura Modelo;
        public TipoCaptura Tipo;
        public int Puntos;
        public string PorQue = "";
        public List<FormatoCaptura> Formatos;   // null = sin abrir
        public string FormatosError = "";
        public int PinesAudio = -1;              // pines de audio en el filtro de video (-1 sin mirar)
        public AudioEntrada Audio;
        public string AudioMotivo = "";
        public bool Simulada;

        // El mejor formato sin comprimir para jugar (resolucion <= tope, >= 59 fps), o comprimido si no hay otro
        public FormatoCaptura Mejor(int topeAlto, double fpsMin) { return Mejor(topeAlto, fpsMin, 1080); }

        // (1.2.2, UNIVERSAL) Orden: (1) que llegue a los fps pedidos DE VERDAD (los "malos" del catalogo con sus fps reales: la GC573 a 4K
        // en RGB24 lista 60 y da 53); (2) sin comprimir (MJPEG / H.264 cuestan CPU, una imagen de retraso y el motor propio no los abre);
        // (3) la resolucion mas alta hasta el tope; (4) el formato: YUY2 (4:2:2) > NV12 (4:2:0, el que cabe por USB 3.0 a 4K30/1440p60) >
        // RGB24 > RGB32 > UYVY > P010 (10 bits: HDR) > MJPEG. Comprimidos por encima de topeComprimido (alto) fuera: descomprimir MJPEG a 4K
        // en la CPU (mpv sin hwdec) no llega a 30 en muchos PC. Los formatos previstos del catalogo (sin abrirla) cuentan igual.
        public FormatoCaptura Mejor(int topeAlto, double fpsMin, int topeComprimido)
        {
            List<FormatoCaptura> l = FormatosParaElegir();
            if (l == null || l.Count == 0) return null;
            FormatoCaptura best = null; double bestScore = -1;
            foreach (FormatoCaptura f in l)
            {
                if (f.Alto > topeAlto || f.Alto < 360) continue;
                if (f.Comprimido && f.Alto > topeComprimido) continue;
                // (P010: ni mpv -ffmpeg dshow no tiene P010- ni el motor lo abren; es la captura HDR de 10 bits y la imagen va en SDR)
                if (f.Subtipo == "P010") continue;
                double fps = FpsDe(f);
                if (fps <= 0) continue;   // (lo que el catalogo sabe que no se puede usar)
                // (por encima de lo pedido los fps ya no cuentan: YUY2 a 60 antes que NV12 a 120 para el modo 60; pero mejor uno que admita
                // 59,94-60 tal cual: la GC573 lista 1440p "a 120" fijos y "59,94-144"; con el de 120 fijos se pediria 120 para el modo 60)
                double mn = f.FpsMin > 0 ? f.FpsMin : fps;
                bool modo60 = fpsMin >= 59 && fpsMin <= 61;
                bool justo = modo60 && mn <= 60.05 && fps >= 59.9;
                // (para el modo 60 solo vale un formato que de 59,94-60 de verdad: uno que solo lista 120 o 240 fijos no es "de 60")
                bool llega = fps >= fpsMin && (!modo60 || justo);
                // (16:9 por delante de la resolucion: la consola manda 16:9; la GC573 lista 3440x1440 y 2560x1080 -panoramicas- con el mismo
                // alto que 2560x1440 y 1920x1080 y salian ellas)
                bool ancho169 = Math.Abs(f.Ancho * 9L - f.Alto * 16L) <= f.Alto * 16L / 50;
                double s = (llega ? 1000000 : Math.Min(fps, 240) / 10.0) + (f.Comprimido ? 0 : 500000) + (ancho169 ? 300000 : 0) + f.Alto * 100.0 + SubtipoPuntos(f.Subtipo) + (justo ? 50 : 0);
                if (s > bestScore) { bestScore = s; best = f; }
            }
            return best;
        }

        // Los formatos leidos o, sin abrirla, los previstos del catalogo (null si no hay ni uno ni otro)
        public List<FormatoCaptura> FormatosParaElegir()
        {
            if (Formatos != null && Formatos.Count > 0) return Formatos;
            if (Modelo != null && Modelo.Modos.Length > 0) return Modelo.Previstos();
            return null;
        }

        // Las de verdad: lo que lista con la correccion del catalogo (Malos)
        public double FpsDe(FormatoCaptura f) { return Modelo != null ? Modelo.FpsReal(f.Alto, f.Subtipo, f.FpsMax) : f.FpsMax; }

        public static int SubtipoPuntos(string s)
        {
            switch (s) { case "YUY2": return 40; case "NV12": return 36; case "RGB24": return 30; case "RGB32": return 28; case "UYVY": return 25; case "P010": return 20; case "MJPG": return 5; default: return 0; }
        }

        // (1.2.2) Los formatos que el motor propio sabe abrir (src\Motor\Engine\CaptureSource.cs: RGB24, RGB32, YUY2, NV12). Con otro
        // (MJPEG, H.264, UYVY, P010...) la imagen va por mpv (que los descomprime / convierte) + Magpie
        // (23-11, 1.2.4 SINMPV: y MJPEG, con el MJPEG Decompressor de Windows en el motor; antes iba por mpv)
        public static bool MotorLoAbre(string subtipo) { return subtipo == "YUY2" || subtipo == "NV12" || subtipo == "RGB24" || subtipo == "RGB32" || subtipo == "MJPG" || subtipo == "MJPEG"; }
    }

    public static class DetectorCapturadoras
    {
        // ------------------------------------------------------------------ catalogo (lo que se sabe sin abrir; el asistente lo confirma)
        // (1.2.2, UNIVERSAL) el catalogo es una tabla de datos aparte: CatalogoCapturas.cs (modelos, formatos previstos, modos malos, rarezas)
        public static readonly List<ModeloCaptura> Catalogo = CatalogoCapturas.Modelos;

        static readonly string[] PalabrasCaptura = { "capture", "captura", "hdmi", "game", "live gamer", "cam link", "usb video", "uvc", "grabber", "video capture", "usb3.0 capture", "usb3. 0 capture" };
        static readonly string[] PalabrasWebcam = { "webcam", "camera", "cámara", "camara", "facecam", "brio", "c920", "c922", "c930", "c270", "streamcam", "lifecam", "kiyo", "integrated", "ir cam", "user facing", "front", "rear" };
        static readonly string[] PalabrasVirtual = { "obs", "virtual", "nvidia broadcast", "snap camera", "xsplit", "streamlabs", "manycam", "droidcam", "camo", "e2esoft", "vcam", "splitcam", "iriun", "epoccam" };

        public static List<CapturadoraInfo> Detectar(OpcionesDeteccion op, List<AudioEntrada> audios, List<string> errores)
        {
            List<CapturadoraInfo> l = new List<CapturadoraInfo>();
            List<AudioEntrada> dsAudio = new List<AudioEntrada>();
            List<string[]> mf = new List<string[]>();
            Exception err = null;
            // COM en un hilo MTA propio (la app de bandeja va en STA), con tope de tiempo: un driver colgado no para la deteccion
            Thread t = new Thread(delegate()
            {
                try
                {
                    Enumerar(Ds.CLSID_VideoInputDeviceCategory, delegate(string name, string path, string clsid)
                    {
                        CapturadoraInfo c = new CapturadoraInfo(); c.Nombre = name; c.DevicePath = path; c.Clsid = clsid; l.Add(c);
                    });
                    int hr = Enumerar(Ds.CLSID_AudioInputDeviceCategory, delegate(string name, string path, string clsid)
                    {
                        AudioEntrada a = new AudioEntrada(); a.Nombre = name; a.Origen = "dshow"; a.Id = path; a.Clsid = clsid; dsAudio.Add(a);
                    });
                    if (hr != 0) lock (errores) errores.Add("DirectShow (audio): CreateClassEnumerator 0x" + hr.ToString("X8"));
                }
                catch (Exception ex) { err = ex; }
                if (op.UsarMediaFoundation) { try { MediaFoundation(mf); } catch (Exception ex) { lock (errores) errores.Add("Media Foundation: " + ex.Message); } }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            if (!t.Join(op.TopeMs)) { errores.Add("DirectShow: sin respuesta en " + op.TopeMs + " ms"); return new List<CapturadoraInfo>(); }
            if (err != null) errores.Add("DirectShow: " + err.Message);

            // Media Foundation: las que solo salen ahi (camaras del "Frame Server", algunas UVC) y las que estan en las dos
            foreach (string[] m in mf)
            {
                CapturadoraInfo c = l.Find(delegate(CapturadoraInfo x) { return string.Equals(x.DevicePath, m[1], StringComparison.OrdinalIgnoreCase) || (x.DevicePath.Length == 0 && x.Nombre == m[0]); });
                if (c != null) { c.EnMediaFoundation = true; continue; }
                c = new CapturadoraInfo(); c.Nombre = m[0]; c.DevicePath = m[1]; c.EnDirectShow = false; c.EnMediaFoundation = true; l.Add(c);
            }

            foreach (CapturadoraInfo c in l) { Nodo(c); Clasificar(c); }
            foreach (AudioEntrada a in dsAudio)
            {
                a.Instancia = Nat.InstanceFromInterfacePath(a.Id) ?? "";
                a.Cadena = Nat.Chain(a.Instancia);
                uint dn = Nat.DevNode(a.Instancia); a.Contenedor = Nat.ContainerId(dn);
            }
            audios.AddRange(dsAudio);
            try { audios.AddRange(Wasapi()); } catch (Exception ex) { errores.Add("Audio (registro): " + ex.Message); }

            foreach (CapturadoraInfo c in l) { if (c.Tipo != TipoCaptura.Virtual) EmparejarAudio(c, audios); else c.AudioMotivo = "-"; }

            // Formatos: solo si se pide (abre el dispositivo)
            if (op.AbrirCapturadoras)
                foreach (CapturadoraInfo c in l)
                {
                    if (c.Tipo == TipoCaptura.Virtual || !c.EnDirectShow) continue;
                    if (op.AntesDeAbrir != null && !op.AntesDeAbrir(c.Nombre)) { c.FormatosError = Tx.T("sin permiso para abrirla ahora", "not allowed to open it now"); continue; }
                    try { Formatos(c, op.TopeMs); }
                    catch (Exception ex) { c.FormatosError = ex.Message; }
                    finally { if (op.DespuesDeAbrir != null) op.DespuesDeAbrir(c.Nombre); }
                    EmparejarAudio(c, audios);   // con los pines de audio ya vistos
                }

            l.Sort(delegate(CapturadoraInfo a, CapturadoraInfo b) { return b.Puntos.CompareTo(a.Puntos); });
            return l;
        }

        delegate void Visita(string name, string path, string clsid);

        static int Enumerar(Guid cat, Visita v)
        {
            IDsCreateDevEnum de = (IDsCreateDevEnum)Ds.CoCreate(Ds.CLSID_SystemDeviceEnum);
            IDsEnumMonikers em = null;
            int n = 0;
            try
            {
                IntPtr pe;
                int hr = de.CreateClassEnumerator(ref cat, out pe, 0);
                if (hr != 0 || pe == IntPtr.Zero) return hr == 1 ? 0 : hr;   // S_FALSE: no hay ninguno
                em = (IDsEnumMonikers)Marshal.GetObjectForIUnknown(pe);
                Marshal.Release(pe);
                for (int guard = 0; guard < 64; guard++)
                {
                    IntPtr pm; uint got;
                    if (em.Next(1, out pm, out got) != 0 || got == 0 || pm == IntPtr.Zero) break;
                    IDsMonikerX m = (IDsMonikerX)Marshal.GetObjectForIUnknown(pm);
                    Marshal.Release(pm);
                    try
                    {
                        Guid iid = Ds.IID_IPropertyBag;
                        IntPtr pb;
                        if (m.BindToStorage(IntPtr.Zero, IntPtr.Zero, ref iid, out pb) < 0 || pb == IntPtr.Zero) continue;
                        IDsPropertyBag bag = (IDsPropertyBag)Marshal.GetObjectForIUnknown(pb);
                        Marshal.Release(pb);
                        try { v(Ds.ReadBag(bag, "FriendlyName") ?? "", Ds.ReadBag(bag, "DevicePath") ?? "", Ds.ReadBag(bag, "CLSID") ?? ""); n++; }
                        finally { Ds.Release(bag); }
                    }
                    finally { Ds.Release(m); }
                }
            }
            finally { Ds.Release(em); Ds.Release(de); }
            return 0;
        }

        // ------------------------------------------------------------------ Media Foundation (solo la lista)
        [DllImport("mfplat.dll")] static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] static extern int MFShutdown();
        [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IntPtr attrs, uint size);
        [DllImport("mf.dll")] static extern int MFEnumDeviceSources(IntPtr attrs, out IntPtr activates, out uint count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetGuidFn(IntPtr self, ref Guid key, ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetAllocStrFn(IntPtr self, ref Guid key, out IntPtr str, out uint len);
        static readonly Guid MfSourceType = new Guid("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
        static readonly Guid MfVidcap = new Guid("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
        static readonly Guid MfFriendly = new Guid("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
        static readonly Guid MfSymLink = new Guid("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");

        static void MediaFoundation(List<string[]> res)
        {
            if (MFStartup(0x00020070, 1 /*LITE*/) < 0) return;
            IntPtr attrs = IntPtr.Zero, arr = IntPtr.Zero; uint n = 0;
            try
            {
                if (MFCreateAttributes(out attrs, 1) < 0) return;
                Guid k = MfSourceType, v = MfVidcap;
                Nat.Vt<SetGuidFn>(attrs, 24)(attrs, ref k, ref v);
                if (MFEnumDeviceSources(attrs, out arr, out n) < 0) return;
                for (int i = 0; i < n; i++)
                {
                    IntPtr act = Marshal.ReadIntPtr(arr, i * IntPtr.Size);
                    try { res.Add(new string[] { MfStr(act, MfFriendly), MfStr(act, MfSymLink) }); }
                    finally { Nat.Release(act); }
                }
            }
            finally
            {
                if (arr != IntPtr.Zero) Nat.CoTaskMemFree(arr);
                Nat.Release(attrs);
                MFShutdown();
            }
        }

        static string MfStr(IntPtr obj, Guid key)
        {
            IntPtr s; uint len;
            if (Nat.Vt<GetAllocStrFn>(obj, 13)(obj, ref key, out s, out len) < 0 || s == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(s) ?? ""; } finally { Nat.CoTaskMemFree(s); }
        }

        // ------------------------------------------------------------------ audio de Windows (registro, sin abrir)
        static List<AudioEntrada> Wasapi()
        {
            List<AudioEntrada> l = new List<AudioEntrada>();
            const string root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";
            foreach (string k in Nat.SubKeys(Registry.LocalMachine, root))
            {
                int state = Nat.RegInt(Registry.LocalMachine, root + "\\" + k, "DeviceState", 0);
                if ((state & 1) == 0) continue;   // solo las activas
                string pk = root + "\\" + k + @"\Properties";
                string ep = Nat.RegStr(Registry.LocalMachine, pk, "{a45c254e-df1c-4efd-8020-67d146a850e0},2") ?? "";
                string ad = Nat.RegStr(Registry.LocalMachine, pk, "{026e516e-b814-414b-83cd-856d6fef4822},2") ?? "";
                AudioEntrada a = new AudioEntrada();
                a.Origen = "wasapi";
                a.Id = "{0.0.1.00000000}." + k;
                a.Nombre = ad.Length > 0 ? ep + " (" + ad + ")" : ep;
                a.Instancia = @"SWD\MMDEVAPI\" + a.Id.ToUpperInvariant();
                a.Cadena = Nat.Chain(a.Instancia);
                uint dn = Nat.DevNode(a.Instancia);
                a.Contenedor = Nat.ContainerId(dn);
                l.Add(a);
            }
            return l;
        }

        // ------------------------------------------------------------------ nodo y clasificacion
        static void Nodo(CapturadoraInfo c)
        {
            c.Instancia = Nat.InstanceFromInterfacePath(c.DevicePath) ?? "";
            if (c.Instancia.Length == 0) { c.Bus = c.DevicePath.Length == 0 ? "software" : "?"; return; }
            c.Bus = c.Instancia.Split('\\')[0];
            c.Cadena = Nat.Chain(c.Instancia);
            uint dn = Nat.DevNode(c.Instancia);
            c.NombrePnp = Nat.DevProp(dn, Nat.CM_DRP_FRIENDLYNAME) ?? Nat.DevProp(dn, Nat.CM_DRP_DEVICEDESC) ?? "";
            c.Fabricante = Nat.DevProp(dn, Nat.CM_DRP_MFG) ?? "";
            c.Servicio = Nat.DevProp(dn, Nat.CM_DRP_SERVICE) ?? "";
            c.Contenedor = Nat.ContainerId(dn);
            int vid, pid;
            // el VID/PID del propio nodo o del primer padre que lo tenga (las de PCI con hijos de software)
            foreach (string id in c.Cadena)
                if (Nat.UsbIds(id, out vid, out pid)) { c.Vid = vid; c.Pid = pid; if (id.StartsWith("USB") || id.StartsWith("PCI")) c.Bus = id.Split('\\')[0]; break; }
        }

        public static void Clasificar(CapturadoraInfo c)
        {
            string n = (c.Nombre + " | " + c.NombrePnp).ToLowerInvariant();
            int pts = 0;
            List<string> why = new List<string>();
            // catalogo: por VID/PID exactos, luego por nombre
            ModeloCaptura best = null;
            foreach (ModeloCaptura m in Catalogo)
                if (m.Vid != 0 && m.Vid == c.Vid && (m.Pid == 0 || m.Pid == c.Pid)) { if (best == null || (best.Pid == 0 && m.Pid != 0)) best = m; }
            if (best == null || best.Pid == 0)
                foreach (ModeloCaptura m in Catalogo)
                {
                    bool hit = false;
                    foreach (string p in m.Patrones) if (n.Contains(p.ToLowerInvariant())) { hit = true; break; }
                    // (1.2.2: tambien un modelo concreto con su VID:PID en el catalogo -m.Pid != 0- gana a la generica de la marca por VID: un
                    // PID que no esta en la tabla, p. ej. el compuesto 07CA:D553 de la GC553G2)
                    if (hit && (best == null || best.Patrones.Length == 0 || m.Vid == 0 || m.Pid != 0)) { best = m; break; }
                }
            // (1.2.2, UNIVERSAL) dos modelos con el mismo VID:PID (la GC570D es 1461:0054 como la GC573, con otro subsistema): si el nombre no
            // es el del modelo del VID:PID y si el de otro concreto de la misma marca, manda el nombre (las genericas de marca no cuentan)
            if (best != null && best.Pid != 0 && best.Patrones.Length > 0 && !Coincide(best, n))
                foreach (ModeloCaptura m in Catalogo)
                    if (m != best && m.Marca == best.Marca && m.Patrones.Length > 0 && !(m.Vid != 0 && m.Pid == 0) && Coincide(m, n)) { best = m; break; }
            c.Modelo = best;
            if (best != null) { pts += best.Pid != 0 || best.Vid == 0 ? 100 : 70; why.Add(Tx.T("modelo conocido: ", "known model: ") + best.Marca + " " + best.Modelo); }
            foreach (string w in PalabrasCaptura) if (n.Contains(w)) { pts += 40; why.Add("«" + w + "»"); break; }
            bool cam = false, virt = false;
            foreach (string w in PalabrasWebcam) if (n.Contains(w) && !n.Contains("cam link")) { cam = true; why.Add(Tx.T("parece webcam", "looks like a webcam") + " («" + w + "»)"); break; }
            foreach (string w in PalabrasVirtual) if (n.Contains(w)) { virt = true; why.Add(Tx.T("virtual", "virtual") + " («" + w + "»)"); break; }
            if (c.Bus == "software" || c.Bus == "SWD" || c.Bus == "ROOT") { virt = virt || c.Bus == "software"; pts -= 30; }
            if (c.Bus == "PCI") pts += 20;
            if (virt) pts -= 150;
            if (cam && best == null) pts -= 80;
            c.Puntos = pts;
            c.Tipo = virt ? TipoCaptura.Virtual : pts >= 40 ? TipoCaptura.Capturadora : cam ? TipoCaptura.Webcam : TipoCaptura.Desconocido;
            c.PorQue = string.Join(", ", why.ToArray());
        }

        static bool Coincide(ModeloCaptura m, string nombreMinusculas)
        {
            foreach (string p in m.Patrones) if (nombreMinusculas.Contains(p.ToLowerInvariant())) return true;
            return false;
        }

        // ------------------------------------------------------------------ emparejar el audio
        static readonly string[] Genericas = { "usb", "video", "audio", "capture", "device", "digital", "interface", "hd", "the", "de", "input", "entrada", "micrófono", "microfono", "line", "línea", "linea", "1", "2", "3" };

        public static void EmparejarAudio(CapturadoraInfo c, List<AudioEntrada> audios)
        {
            AudioEntrada best = null; int bestPts = 0; string why = "";
            // el aparato fisico de la capturadora: el padre "compuesto" USB (sin &MI_xx) o el propio nodo PCI
            string raiz = null;
            foreach (string id in c.Cadena)
                if ((id.StartsWith("USB\\") && !id.Contains("&MI_")) || id.StartsWith("PCI\\")) { raiz = id; break; }
            foreach (AudioEntrada a in audios)
            {
                int p = 0; string w = "";
                if (a.Origen == "dshow" && string.Equals(a.Nombre, c.Nombre, StringComparison.OrdinalIgnoreCase))
                { p = 100; w = Tx.T("el mismo nombre en DirectShow (el audio va en el filtro de la capturadora)", "same DirectShow name"); }
                else if (raiz != null && a.Cadena.Contains(raiz))
                { p = 90; w = Tx.T("el mismo aparato (", "same device (") + raiz + ")"; }
                else if (c.Contenedor != Guid.Empty && c.Contenedor != Nat.ContenedorPc && a.Contenedor == c.Contenedor)
                { p = 85; w = Tx.T("el mismo contenedor de Windows", "same Windows container"); }
                else if (c.Vid != 0 && a.Cadena.Count > 0)
                {
                    foreach (string id in a.Cadena)
                    {
                        int v, d;
                        if (Nat.UsbIds(id, out v, out d) && v == c.Vid && d == c.Pid) { p = 75; w = Tx.T("el mismo VID/PID", "same VID/PID"); break; }
                    }
                }
                if (p == 0)
                {
                    int common = Comunes(c.Nombre + " " + c.NombrePnp, a.Nombre);
                    if (common > 0) { p = 30 + 15 * Math.Min(3, common); w = Tx.T("nombre parecido", "similar name"); }
                }
                // un endpoint de Windows y su copia de DirectShow (mismo nombre recortado): mejor el de DirectShow si empata
                if (p > bestPts || (p == bestPts && p > 0 && a.Origen == "dshow" && best != null && best.Origen != "dshow")) { bestPts = p; best = a; why = w; }
            }
            // el audio en pines del filtro de video (sabido por el catalogo, o visto al abrirla): como mpv, dshow:audio=<nombre> y audio_pin_name
            bool enFiltro = c.PinesAudio > 0 || (c.PinesAudio < 0 && c.Modelo != null && c.Modelo.AudioEnFiltro);
            if (enFiltro && bestPts < 100)
            {
                AudioEntrada a = new AudioEntrada();
                a.Nombre = c.Nombre; a.Origen = "dshow-pin"; a.Id = c.DevicePath; a.Instancia = c.Instancia; a.Cadena = c.Cadena;
                best = a; bestPts = 95;
                why = c.PinesAudio > 0 ? Tx.T("pines de audio del propio filtro de video (", "audio pins on the video filter (") + c.PinesAudio + Tx.T("; el que suena lo prueba el asistente)", "; the wizard tests which one)")
                                       : Tx.T("pines de audio del propio filtro de video (catálogo; el que suena lo prueba el asistente)", "audio pins on the video filter (catalog)");
            }
            if (best != null && bestPts >= 45) { c.Audio = best; c.AudioMotivo = why; }
            else c.AudioMotivo = Tx.T("sin audio emparejado: el asistente lo pregunta", "no paired audio: the wizard will ask");
        }

        static int Comunes(string a, string b)
        {
            string[] ta = a.ToLowerInvariant().Split(new char[] { ' ', '(', ')', '-', '_', ',', '.', '|' }, StringSplitOptions.RemoveEmptyEntries);
            string lb = b.ToLowerInvariant();
            int n = 0;
            List<string> seen = new List<string>();
            foreach (string t in ta)
            {
                if (t.Length < 3 || Array.IndexOf(Genericas, t) >= 0 || seen.Contains(t)) continue;
                seen.Add(t);
                if (lb.Contains(t)) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ formatos (ABRE el dispositivo)
        // Para el asistente: los formatos de una (la abre) y su audio otra vez con los pines ya vistos. Devuelve "" o el error
        public static string LeerFormatos(CapturadoraInfo c, List<AudioEntrada> audios, int tope)
        {
            if (c == null || c.Simulada) return "";
            // (1.2.02) si no se pueden leer, lista vacia (no null): el asistente volvia a abrirla sin parar (en bucle si no es de DirectShow)
            if (!c.EnDirectShow) { if (c.Formatos == null) c.Formatos = new List<FormatoCaptura>(); return Tx.T("no es de DirectShow", "not a DirectShow device"); }
            try { Formatos(c, tope); c.FormatosError = ""; }
            catch (Exception ex) { c.FormatosError = ex.Message; }
            if (c.Formatos == null) c.Formatos = new List<FormatoCaptura>();
            EmparejarAudio(c, audios);
            return c.FormatosError;
        }

        static void Formatos(CapturadoraInfo c, int tope)
        {
            Exception err = null;
            List<FormatoCaptura> res = new List<FormatoCaptura>();
            int audioPins = -1;
            Thread t = new Thread(delegate()
            {
                try { audioPins = FormatosMta(c, res); }
                catch (Exception ex) { err = ex; }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            if (!t.Join(tope)) throw new TimeoutException(Tx.T("el driver no contesta en ", "driver did not answer in ") + tope + " ms");
            if (err != null) throw err;
            c.Formatos = res;
            c.PinesAudio = audioPins;
        }

        static int FormatosMta(CapturadoraInfo c, List<FormatoCaptura> res)
        {
            IDsBaseFilter f = null;
            IDsGraphBuilder g = null;
            int audioPins = 0;
            try
            {
                // el mismo dispositivo por su DevicePath (o por el nombre si no tiene)
                IntPtr pf = IntPtr.Zero;
                IDsCreateDevEnum de = (IDsCreateDevEnum)Ds.CoCreate(Ds.CLSID_SystemDeviceEnum);
                try
                {
                    Guid cat = Ds.CLSID_VideoInputDeviceCategory;
                    IntPtr pe;
                    if (de.CreateClassEnumerator(ref cat, out pe, 0) != 0 || pe == IntPtr.Zero) throw new InvalidOperationException("sin dispositivos");
                    IDsEnumMonikers em = (IDsEnumMonikers)Marshal.GetObjectForIUnknown(pe);
                    Marshal.Release(pe);
                    try
                    {
                        for (int guard = 0; guard < 64 && pf == IntPtr.Zero; guard++)
                        {
                            IntPtr pm; uint got;
                            if (em.Next(1, out pm, out got) != 0 || got == 0 || pm == IntPtr.Zero) break;
                            IDsMonikerX m = (IDsMonikerX)Marshal.GetObjectForIUnknown(pm);
                            Marshal.Release(pm);
                            try
                            {
                                Guid iid = Ds.IID_IPropertyBag; IntPtr pb;
                                if (m.BindToStorage(IntPtr.Zero, IntPtr.Zero, ref iid, out pb) < 0 || pb == IntPtr.Zero) continue;
                                IDsPropertyBag bag = (IDsPropertyBag)Marshal.GetObjectForIUnknown(pb);
                                Marshal.Release(pb);
                                string name = Ds.ReadBag(bag, "FriendlyName") ?? "", path = Ds.ReadBag(bag, "DevicePath") ?? "";
                                Ds.Release(bag);
                                bool match = c.DevicePath.Length > 0 ? string.Equals(path, c.DevicePath, StringComparison.OrdinalIgnoreCase) : name == c.Nombre;
                                if (!match) continue;
                                Guid ib = Ds.IID_IBaseFilter;
                                int hr = m.BindToObject(IntPtr.Zero, IntPtr.Zero, ref ib, out pf);
                                if (hr < 0) throw new InvalidOperationException("BindToObject 0x" + hr.ToString("X8"));
                            }
                            finally { Ds.Release(m); }
                        }
                    }
                    finally { Ds.Release(em); }
                }
                finally { Ds.Release(de); }
                if (pf == IntPtr.Zero) throw new InvalidOperationException(Tx.T("no se encuentra", "not found"));
                f = (IDsBaseFilter)Marshal.GetObjectForIUnknown(pf);
                Marshal.Release(pf);
                // los pines de ksproxy solo dan IAMStreamConfig con el filtro en un grafo
                g = (IDsGraphBuilder)Ds.CoCreate(Ds.CLSID_FilterGraph);
                int ah = g.AddFilter(f, "Capture");
                if (ah < 0) throw new InvalidOperationException("AddFilter 0x" + ah.ToString("X8"));
                IntPtr pen;
                if (f.EnumPins(out pen) < 0 || pen == IntPtr.Zero) throw new InvalidOperationException("EnumPins");
                IDsEnumPins en = (IDsEnumPins)Marshal.GetObjectForIUnknown(pen);
                Marshal.Release(pen);
                try
                {
                    for (int guard = 0; guard < 16; guard++)
                    {
                        IntPtr pp; uint got;
                        if (en.Next(1, out pp, out got) != 0 || got == 0 || pp == IntPtr.Zero) break;
                        IDsPin pin = (IDsPin)Marshal.GetObjectForIUnknown(pp);
                        Marshal.Release(pp);
                        try
                        {
                            int dir;
                            IDsStreamConfig sc = pin as IDsStreamConfig;
                            if (pin.QueryDirection(out dir) < 0 || dir != 1 || sc == null) continue;
                            int n, sz;
                            if (sc.GetNumberOfCapabilities(out n, out sz) < 0 || n <= 0) continue;
                            IntPtr scc = Marshal.AllocHGlobal(Math.Max(sz, 256));
                            try
                            {
                                for (int i = 0; i < n && i < 512; i++)
                                {
                                    IntPtr pmt;
                                    if (sc.GetStreamCaps(i, out pmt, scc) < 0 || pmt == IntPtr.Zero) continue;
                                    try
                                    {
                                        DsMediaType mt = (DsMediaType)Marshal.PtrToStructure(pmt, typeof(DsMediaType));
                                        if (mt.majortype == Ds.MEDIATYPE_Audio) { if (i == 0) audioPins++; continue; }
                                        if (mt.majortype != Ds.MEDIATYPE_Video) continue;
                                        FormatoCaptura fc = Leer(mt, sz >= 128 ? scc : IntPtr.Zero);
                                        if (fc != null && !res.Exists(delegate(FormatoCaptura x) { return x.Ancho == fc.Ancho && x.Alto == fc.Alto && x.Subtipo == fc.Subtipo && Math.Abs(x.FpsMax - fc.FpsMax) < 0.01; }))
                                            res.Add(fc);
                                    }
                                    finally { Ds.DeleteMediaType(pmt); }
                                }
                            }
                            finally { Marshal.FreeHGlobal(scc); }
                        }
                        finally { Ds.Release(pin); }
                    }
                }
                finally { Ds.Release(en); }
            }
            finally
            {
                if (g != null && f != null) { try { g.RemoveFilter(f); } catch { } }
                Ds.Release(g); Ds.Release(f);
            }
            res.Sort(delegate(FormatoCaptura a, FormatoCaptura b)
            {
                int k = (b.Ancho * b.Alto).CompareTo(a.Ancho * a.Alto);
                if (k == 0) k = b.FpsMax.CompareTo(a.FpsMax);
                if (k == 0) k = string.CompareOrdinal(a.Subtipo, b.Subtipo);
                return k;
            });
            return audioPins;
        }

        // VIH: AvgTimePerFrame @40, BITMAPINFOHEADER @48; VIH2: @40 y @72. Caps (VIDEO_STREAM_CONFIG_CAPS): MinFrameInterval @104, Max @112
        static FormatoCaptura Leer(DsMediaType mt, IntPtr scc)
        {
            FormatoCaptura f = new FormatoCaptura();
            f.Subtipo = Ds.Subtipo(mt.subtype, out f.Comprimido);
            int bmi;
            if (mt.formattype == Ds.FORMAT_VideoInfo && mt.cbFormat >= 88) bmi = 48;
            else if (mt.formattype == Ds.FORMAT_VideoInfo2 && mt.cbFormat >= 112) bmi = 72;
            else return null;
            IntPtr p = mt.pbFormat;
            long avg = Marshal.ReadInt64(p, 40);
            f.Ancho = Marshal.ReadInt32(p, bmi + 4);
            f.Alto = Math.Abs(Marshal.ReadInt32(p, bmi + 8));
            long mn = avg, mx = avg;
            if (scc != IntPtr.Zero)
            {
                long a = Marshal.ReadInt64(scc, 104), b = Marshal.ReadInt64(scc, 112);
                if (a > 0 && b >= a) { mn = a; mx = b; }
            }
            f.FpsMax = mn > 0 ? Math.Round(1e7 / mn, 3) : 0;
            f.FpsMin = mx > 0 ? Math.Round(1e7 / mx, 3) : 0;
            return f;
        }
    }
}
