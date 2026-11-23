// EsperaSonda.cs - 22-11-2026 (1.2.2, el usuario: "Haz que el pc este mas dormido en el modo consola, que gaste menos").
// La sonda de la espera sin mpv: ConsoleEnhancer.exe --ce-sonda (lo abre la espera, src\Lanzador\Espera.cs, como abria mpv).
//
// Por que: la sonda de mpv (hasta la 1.2.1) tiene la capturadora abierta todo el rato y la GC573 manda 60 imagenes de 1280x720 por
// segundo (1,8 MB cada una) que mpv copia, pasa por 5 hilos y tira, para mirar UNA por segundo. Medido en espera (procesador al 50 %):
// 13,5 % de un nucleo y ~405 cambios de contexto por segundo, la memoria y el PCIe moviendo 110 MB/s sin parar. Abrirla y cerrarla con
// mpv a ratos no sirve: cada apertura de DirectShow (buscar el dispositivo, montar el grafo, leer sus ~150 formatos) cuesta ~0,4 s de
// CPU (medido: con una cada 2 s, peor que tenerla abierta).
// Aqui el grafo (capturadora -> Sample Grabber -> Null Renderer, solo el pin de video) se monta UNA vez y se arranca (Run) un momento
// cada espera_sonda_cada_ms: medido con la GC573, Run ~40 ms, la primera imagen a los ~110 ms, Stop ~85 ms y ~16 ms de CPU por vez
// (a 3,4 GHz). Entre una vez y otra la capturadora no manda nada (ni interrupciones ni copias) y el proceso no se despierta.
// Cada vez: la primera imagen no cuenta (tras un Stop la primera a veces sale negra), luego 2 imagenes que no sean la consola = nada
// (Stop y a dormir). Una imagen que no es "No Signal" ni negro: sigue en marcha y, si 0,4 s despues sigue viendose, la consola (sale
// con 55, como la sonda de mpv). El mismo criterio que scripts\ce_standby.lua (franjas negras arriba y abajo y un recuadro gris en el
// centro = "No Signal"), con la imagen en YUY2 o NV12 pasada a RGB como lo hace mpv (BT.709, rango limitado).
// Codigos: 55 = la consola; 3 = no se ha podido montar o arrancar el grafo (la espera vuelve a la sonda de mpv).
// Pruebas: --ce-sonda-prueba=<carpeta con PNG>[;<informe>] clasifica imagenes guardadas por el mismo camino (RGB -> YUY2 -> RGB).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ConsoleEnhancer.SondaDs;

namespace ConsoleEnhancer.SondaDs
{
    // DirectShow: solo los metodos que se usan (los vtables en su orden; lo de despues no hace falta declararlo)
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICreateDevEnum { [PreserveSig] int CreateClassEnumerator([In] ref Guid cat, out IntPtr enumMoniker, int flags); }

    [ComImport, Guid("00000102-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumMonikerDs { [PreserveSig] int Next(uint n, out IntPtr m, out uint got); }

    [ComImport, Guid("0000000f-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMonikerDs
    {
        [PreserveSig] int GetClassID(out Guid c);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load(IntPtr s);
        [PreserveSig] int Save(IntPtr s, int clear);
        [PreserveSig] int GetSizeMax(out long n);
        [PreserveSig] int BindToObject(IntPtr bc, IntPtr left, [In] ref Guid iid, out IntPtr obj);
        [PreserveSig] int BindToStorage(IntPtr bc, IntPtr left, [In] ref Guid iid, out IntPtr obj);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyBagDs { [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name, ref object v, IntPtr log); }

    [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBaseFilterDs
    {
        [PreserveSig] int GetClassID(out Guid c);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long t);
        [PreserveSig] int GetState(int ms, out int st);
        [PreserveSig] int SetSyncSource(IntPtr clock);
        [PreserveSig] int GetSyncSource(out IntPtr clock);
        [PreserveSig] int EnumPins(out IntPtr e);
    }

    [ComImport, Guid("56a86899-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaFilterDs
    {
        [PreserveSig] int GetClassID(out Guid c);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long t);
        [PreserveSig] int GetState(int ms, out int st);
        [PreserveSig] int SetSyncSource(IntPtr clock);
    }

    [ComImport, Guid("56a868a9-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IGraphBuilderDs
    {
        [PreserveSig] int AddFilter([MarshalAs(UnmanagedType.Interface)] IBaseFilterDs f, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int RemoveFilter([MarshalAs(UnmanagedType.Interface)] IBaseFilterDs f);
        [PreserveSig] int EnumFilters(out IntPtr e);
        [PreserveSig] int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string name, out IntPtr f);
        [PreserveSig] int ConnectDirect([MarshalAs(UnmanagedType.Interface)] IPinDs o, [MarshalAs(UnmanagedType.Interface)] IPinDs i, IntPtr mt);
    }

    [ComImport, Guid("56a868b1-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaControlDs   // (dual: antes, los 4 de IDispatch)
    {
        [PreserveSig] int _GetTypeInfoCount(out uint n);
        [PreserveSig] int _GetTypeInfo(uint i, uint lcid, out IntPtr ti);
        [PreserveSig] int _GetIDsOfNames(IntPtr riid, IntPtr names, uint count, uint lcid, IntPtr ids);
        [PreserveSig] int _Invoke(int id, IntPtr riid, uint lcid, ushort flags, IntPtr dp, IntPtr res, IntPtr ex, IntPtr arg);
        [PreserveSig] int Run();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
    }

    [ComImport, Guid("56a86892-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumPinsDs { [PreserveSig] int Next(uint n, out IntPtr pin, out uint got); }

    [ComImport, Guid("56a86891-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPinDs
    {
        [PreserveSig] int Connect(IntPtr p, IntPtr mt);
        [PreserveSig] int ReceiveConnection(IntPtr p, IntPtr mt);
        [PreserveSig] int Disconnect();
        [PreserveSig] int ConnectedTo(out IntPtr p);
        [PreserveSig] int ConnectionMediaType(IntPtr mt);
        [PreserveSig] int QueryPinInfo(IntPtr info);
        [PreserveSig] int QueryDirection(out int dir);
    }

    [ComImport, Guid("C6E13340-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAMStreamConfigDs
    {
        [PreserveSig] int SetFormat(IntPtr mt);
        [PreserveSig] int GetFormat(out IntPtr mt);
        [PreserveSig] int GetNumberOfCapabilities(out int n, out int size);
        [PreserveSig] int GetStreamCaps(int i, out IntPtr mt, IntPtr caps);
    }

    [ComImport, Guid("56a8689a-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMediaSampleDs
    {
        [PreserveSig] int GetPointer(out IntPtr buf);
        [PreserveSig] int GetSize();
        [PreserveSig] int GetTime(out long a, out long b);
        [PreserveSig] int SetTime(IntPtr a, IntPtr b);
        [PreserveSig] int IsSyncPoint();
        [PreserveSig] int SetSyncPoint(int v);
        [PreserveSig] int IsPreroll();
        [PreserveSig] int SetPreroll(int v);
        [PreserveSig] int GetActualDataLength();
    }

    [ComImport, Guid("6B652FFF-11FE-4fce-92AD-0266B5D7C78F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISampleGrabberDs
    {
        [PreserveSig] int SetOneShot(int v);
        [PreserveSig] int SetMediaType(IntPtr mt);
        [PreserveSig] int GetConnectedMediaType(IntPtr mt);
        [PreserveSig] int SetBufferSamples(int v);
        [PreserveSig] int GetCurrentBuffer(ref int size, IntPtr buf);
        [PreserveSig] int GetCurrentSample(out IntPtr s);
        [PreserveSig] int SetCallback([MarshalAs(UnmanagedType.Interface)] ISampleGrabberCBDs cb, int which);
    }

    [ComImport, Guid("0579154A-2B53-4994-B0D0-E773148EFF85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISampleGrabberCBDs
    {
        [PreserveSig] int SampleCB(double t, IntPtr sample);
        [PreserveSig] int BufferCB(double t, IntPtr buf, int len);
    }

    // 88 bytes en x64
    [StructLayout(LayoutKind.Sequential)]
    public struct AmMediaType
    {
        public Guid majortype, subtype;
        public int bFixedSizeSamples, bTemporalCompression;
        public uint lSampleSize;
        public Guid formattype;
        public IntPtr pUnk;
        public uint cbFormat;
        public IntPtr pbFormat;
    }

    // Lo que llega en el hilo de la capturadora: cada imagen, clasificada al momento (48x27 puntos, ~1300 lecturas)
    [ComVisible(true)]
    public sealed class SondaCb : ISampleGrabberCBDs
    {
        public int W, H, Stride;
        public bool Nv12;
        public volatile int Gen;                    // la vez de ahora (lo de una vez anterior no cuenta)
        readonly object sync = new object();
        readonly Queue<KeyValuePair<long, string>> llegadas = new Queue<KeyValuePair<long, string>>();
        public readonly AutoResetEvent Llego = new AutoResetEvent(false);
        int genLlegadas;

        public int SampleCB(double t, IntPtr pSample)
        {
            string q;
            try
            {
                IMediaSampleDs s = (IMediaSampleDs)Marshal.GetObjectForIUnknown(pSample);
                try
                {
                    IntPtr buf;
                    q = s.GetPointer(out buf) >= 0 && buf != IntPtr.Zero ? CeSonda.Clasificar(buf, s.GetActualDataLength(), W, H, Stride, Nv12) : "error";
                }
                finally { Marshal.ReleaseComObject(s); }
            }
            catch { q = "error"; }
            long prueba = CeSonda.PruebaImagenQpc;
            if (prueba != 0 && q != "error" && Stopwatch.GetTimestamp() >= prueba) q = "imagen";   // (SOLO PRUEBAS)
            lock (sync)
            {
                if (genLlegadas != Gen) { llegadas.Clear(); genLlegadas = Gen; }
                if (llegadas.Count < 64) llegadas.Enqueue(new KeyValuePair<long, string>(Stopwatch.GetTimestamp(), q));
            }
            Llego.Set();
            return 0;
        }

        public int BufferCB(double t, IntPtr b, int n) { return 0; }

        public bool Sacar(out long qpc, out string q)
        {
            lock (sync)
            {
                if (genLlegadas == Gen && llegadas.Count > 0) { KeyValuePair<long, string> k = llegadas.Dequeue(); qpc = k.Key; q = k.Value; return true; }
            }
            qpc = 0; q = null;
            return false;
        }
    }
}

namespace ConsoleEnhancer
{
    static class CeSonda
    {
        public const int CodigoConsola = 55, CodigoSinGrafo = 3;
        public static long PruebaImagenQpc;   // (SOLO PRUEBAS: CE_PRUEBA_SONDA_CONSOLA_S)
        // Lo pone la sonda en cada vez; la espera (Paso, cada segundo) lo mira: 30 s sin el, la sonda colgada -> se cierra y se abre otra
        public const string Latido = "Local\\ConsoleEnhancer_SondaLatido";
        static readonly Guid ClsidDevEnum = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86"), CatVideo = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86"),
                             ClsidGraph = new Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770"), ClsidGrabber = new Guid("C1F400A0-3F08-11d3-9F0B-006008039E37"),
                             ClsidNull = new Guid("C1F400A4-3F08-11d3-9F0B-006008039E37"), IidFilter = new Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"),
                             IidBag = new Guid("55272A00-42CB-11CE-8135-00AA004BB851"), Video = new Guid("73646976-0000-0010-8000-00AA00389B71"),
                             Yuy2 = new Guid("32595559-0000-0010-8000-00AA00389B71"), Nv12 = new Guid("3231564E-0000-0010-8000-00AA00389B71"),
                             VideoInfo = new Guid("05589f80-c356-11ce-bf01-00aa0055595a"), VideoInfo2 = new Guid("f72a76a0-eb0a-11d0-ace4-0000c0cc16ba");

        // ------------------------------------------------------------------ --ce-sonda
        // --ce-sonda "dispositivo=<nombre>" ["ruta=<device_path>"] [cada_ms=<n>] [formato=<pixel_format del ini>] [medir=1] [simulada=<s>]
        public static int Proceso(string[] args)
        {
            string nombre = "", ruta = "", formato = "";
            int simulada = -1;
            int cada = 2000;
            bool medir = false;
            foreach (string a in args)
            {
                if (a.StartsWith("dispositivo=")) nombre = a.Substring(12);
                else if (a.StartsWith("ruta=")) ruta = a.Substring(5);
                else if (a.StartsWith("formato=")) formato = a.Substring(8);
                else if (a.StartsWith("cada_ms=")) { int n; if (int.TryParse(a.Substring(8), out n)) cada = Math.Max(100, Math.Min(60000, n)); }
                else if (a == "medir=1") medir = true;
                else if (a.StartsWith("simulada=")) { int n; if (int.TryParse(a.Substring(9), out n)) simulada = Math.Max(0, n); }
            }
            // 23-11 (1.2.4, SINMPV) SOLO PRUEBAS (la espera con espera_sonda_fuente=av://lavfi... o --prueba-espera, antes con mpv y una pantalla
            // "No Signal" de mentira): sin capturadora; "No Signal" hasta los simulada s y luego la consola (0 = nunca)
            if (simulada >= 0)
            {
                Log.Write("Sonda de la espera: PRUEBA sin capturadora (simulada=" + simulada + "): la consola a los " + simulada + " s" + (simulada == 0 ? " (nunca)" : ""));
                Stopwatch ss = Stopwatch.StartNew();
                EventWaitHandle lt = null;
                try { lt = new EventWaitHandle(false, EventResetMode.AutoReset, Latido); } catch { }
                while (true)
                {
                    if (lt != null) try { lt.Set(); } catch { }
                    if (simulada > 0 && ss.Elapsed.TotalSeconds >= simulada) { Log.Write("Sonda de la espera: la consola (prueba)"); return CodigoConsola; }
                    Thread.Sleep(cada);
                }
            }
            // SOLO PRUEBAS: CE_PRUEBA_SONDA_CONSOLA_S=<s>: a los s segundos toma lo que llegue por la consola (para probar todo el despertar
            // con la capturadora de verdad y la consola apagada)
            int ps;
            if (int.TryParse(Environment.GetEnvironmentVariable("CE_PRUEBA_SONDA_CONSOLA_S") ?? "", out ps) && ps > 0)
            {
                PruebaImagenQpc = Stopwatch.GetTimestamp() + ps * Stopwatch.Frequency;
                Log.Write("Sonda de la espera: PRUEBA (CE_PRUEBA_SONDA_CONSOLA_S=" + ps + "): a los " + ps + " s lo que llegue cuenta como la consola");
            }
            int r = CodigoSinGrafo;
            // (DirectShow en un hilo MTA, como el motor: las muestras llegan en el hilo de la capturadora)
            Thread t = new Thread(delegate () { try { r = Bucle(nombre, ruta, formato, cada, medir); } catch (Exception ex) { Log.Error("Sonda de la espera", ex); r = CodigoSinGrafo; } });
            t.SetApartmentState(ApartmentState.MTA);
            t.Name = "Sonda";
            t.Start();
            t.Join();
            return r;
        }

        sealed class Grafo
        {
            public IGraphBuilderDs G;
            public IMediaControlDs Mc;
            public SondaCb Cb;
            public string Detalle = "";
        }

        static int Bucle(string nombre, string ruta, string formato, int cada, bool medir)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string porque;
            Grafo g = Montar(nombre, ruta, formato, out porque);
            if (g == null) { Log.Write("Sonda de la espera: sin grafo de DirectShow (" + porque + "): la espera lo vuelve a intentar"); return CodigoSinGrafo; }
            Log.Write("Sonda de la espera: grafo de " + nombre + " montado en " + sw.ElapsedMilliseconds + " ms (" + g.Detalle + "); la mira un momento cada " + cada + " ms");
            Process me = Process.GetCurrentProcess();
            int vez = 0, fallos = 0;
            double cpuAntes = me.TotalProcessorTime.TotalMilliseconds;
            long primeraSum = 0; int primeraN = 0;
            EventWaitHandle latido = null;
            try { latido = new EventWaitHandle(false, EventResetMode.AutoReset, Latido); } catch { }
            while (true)
            {
                vez++;
                long primera;
                int muestras;
                string q = Mirar(g, out primera, out muestras);
                if (latido != null) try { latido.Set(); } catch { }   // (la espera ve que sigue viva: un Run o un Stop colgados no la dejan sorda)
                if (primera >= 0) { primeraSum += primera; primeraN++; }
                if (q == "consola") { Log.Write("Sonda de la espera: la consola (vez " + vez + ")"); return CodigoConsola; }
                if (q == "error") { if (++fallos >= 3) { Log.Write("Sonda de la espera: el grafo no arranca (3 veces seguidas): la espera lo vuelve a intentar"); return CodigoSinGrafo; } }
                else fallos = 0;
                if (medir || vez == 1 || vez == 3 || vez % 1000 == 0)
                {
                    me.Refresh();
                    double cpu = me.TotalProcessorTime.TotalMilliseconds - cpuAntes;
                    Log.Write("Sonda de la espera: vez " + vez + ": " + q + " (" + muestras + " imagenes, la primera a los " + primera + " ms); de media " +
                              (primeraN > 0 ? primeraSum / primeraN : -1) + " ms hasta la primera y " + (cpu / vez).ToString("0.0", CultureInfo.InvariantCulture) + " ms de CPU por vez");
                }
                Thread.Sleep(cada);
            }
        }

        // Una vez: Run, imagenes hasta decidir, Stop. "nada", "consola" o "error". primera = ms hasta la primera imagen (-1 = ninguna)
        static string Mirar(Grafo g, out long primera, out int muestras)
        {
            primera = -1; muestras = 0;
            g.Cb.Gen++;
            long t0 = Stopwatch.GetTimestamp();
            int hr = g.Mc.Run();
            if (hr < 0) { try { g.Mc.Stop(); } catch { } return "error"; }
            string res = "nada";
            try
            {
                int nada = 0;
                long imagenDesde = 0;
                while (true)
                {
                    long ms = Ms(Stopwatch.GetTimestamp() - t0);
                    // sin imagenes en 2 s (otra capturadora que no manda nada sin senal): nada; con la consola a medio confirmar, hasta 3 s
                    if (ms > (imagenDesde != 0 ? 3000 : 2000)) break;
                    long qpc; string q;
                    if (!g.Cb.Sacar(out qpc, out q)) { g.Cb.Llego.WaitOne(100); continue; }
                    muestras++;
                    if (muestras == 1) { primera = Ms(qpc - t0); continue; }   // (la primera no cuenta: tras un Stop a veces sale negra)
                    if (q == "imagen")
                    {
                        if (imagenDesde == 0) imagenDesde = qpc;
                        else if (Ms(qpc - imagenDesde) >= 400) { res = "consola"; break; }
                        continue;
                    }
                    imagenDesde = 0;
                    if (++nada >= 2) break;
                }
            }
            finally { try { g.Mc.Stop(); } catch { } }
            return res;
        }

        static long Ms(long qpc) { return qpc * 1000 / Stopwatch.Frequency; }

        // El grafo: la capturadora (por su ruta o su nombre) -> Sample Grabber (con aviso por imagen, sin copias) -> Null Renderer, sin reloj
        static Grafo Montar(string nombre, string ruta, string formato, out string porque)
        {
            porque = "";
            IBaseFilterDs cap = Dispositivo(nombre, ruta);
            if (cap == null) { porque = "no esta la capturadora " + nombre; return null; }
            Grafo r = new Grafo();
            r.G = (IGraphBuilderDs)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidGraph, true));
            if (r.G.AddFilter(cap, "Capture") < 0) { porque = "AddFilter"; return null; }
            IPinDs pin = PinVideo(cap);
            if (pin == null) { porque = "sin pin de video con IAMStreamConfig"; return null; }
            IAMStreamConfigDs sc = (IAMStreamConfigDs)pin;
            int w, h, stride; bool nv12;
            IntPtr pmt = Formato(sc, formato, out w, out h, out stride, out nv12);
            if (pmt == IntPtr.Zero) { porque = "sin formato YUY2 / NV12"; return null; }
            IntPtr gmt = IntPtr.Zero;
            try
            {
                int hr = sc.SetFormat(pmt);
                if (hr < 0) { porque = "SetFormat 0x" + hr.ToString("X8"); return null; }
                AmMediaType mt = (AmMediaType)Marshal.PtrToStructure(pmt, typeof(AmMediaType));
                IBaseFilterDs grabF = (IBaseFilterDs)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidGrabber, true));
                ISampleGrabberDs grab = (ISampleGrabberDs)grabF;
                AmMediaType gm = new AmMediaType();
                gm.majortype = Video; gm.subtype = mt.subtype; gm.formattype = mt.formattype;
                gmt = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(AmMediaType)));
                Marshal.StructureToPtr(gm, gmt, false);
                grab.SetMediaType(gmt);
                grab.SetBufferSamples(0);
                grab.SetOneShot(0);
                r.Cb = new SondaCb();
                r.Cb.W = w; r.Cb.H = h; r.Cb.Stride = stride; r.Cb.Nv12 = nv12;
                if (grab.SetCallback(r.Cb, 0) < 0) { porque = "SetCallback"; return null; }
                r.G.AddFilter(grabF, "Grabber");
                IBaseFilterDs nul = (IBaseFilterDs)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidNull, true));
                r.G.AddFilter(nul, "Null");
                IPinDs gIn = Pin(grabF, 0), gOut = Pin(grabF, 1), nIn = Pin(nul, 0);
                if (gIn == null || gOut == null || nIn == null) { porque = "sin pines del Sample Grabber / Null Renderer"; return null; }
                hr = r.G.ConnectDirect(pin, gIn, pmt);
                if (hr < 0) { porque = "ConnectDirect 0x" + hr.ToString("X8"); return null; }
                hr = r.G.ConnectDirect(gOut, nIn, IntPtr.Zero);
                if (hr < 0) { porque = "ConnectDirect (null) 0x" + hr.ToString("X8"); return null; }
                ((IMediaFilterDs)r.G).SetSyncSource(IntPtr.Zero);   // (sin reloj: ni temporizadores ni esperas, cada imagen al llegar)
                r.Mc = (IMediaControlDs)r.G;
                r.Detalle = w + "x" + h + " " + (nv12 ? "NV12" : "YUY2");
                return r;
            }
            finally
            {
                if (gmt != IntPtr.Zero) Marshal.FreeHGlobal(gmt);
                Borrar(pmt);
            }
        }

        static IBaseFilterDs Dispositivo(string nombre, string ruta)
        {
            ICreateDevEnum de = (ICreateDevEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidDevEnum, true));
            Guid cat = CatVideo;
            IntPtr pe;
            if (de.CreateClassEnumerator(ref cat, out pe, 0) != 0 || pe == IntPtr.Zero) return null;
            IEnumMonikerDs em = (IEnumMonikerDs)Marshal.GetObjectForIUnknown(pe);
            Marshal.Release(pe);
            IBaseFilterDs porNombre = null;
            while (true)
            {
                IntPtr pm; uint got;
                if (em.Next(1, out pm, out got) != 0 || got == 0 || pm == IntPtr.Zero) break;
                IMonikerDs m = (IMonikerDs)Marshal.GetObjectForIUnknown(pm);
                Marshal.Release(pm);
                string fn = "", dp = "";
                Guid ib = IidBag;
                IntPtr pb;
                if (m.BindToStorage(IntPtr.Zero, IntPtr.Zero, ref ib, out pb) >= 0 && pb != IntPtr.Zero)
                {
                    IPropertyBagDs bag = (IPropertyBagDs)Marshal.GetObjectForIUnknown(pb);
                    Marshal.Release(pb);
                    object v = null;
                    if (bag.Read("FriendlyName", ref v, IntPtr.Zero) >= 0) fn = (v as string) ?? "";
                    v = null;
                    if (bag.Read("DevicePath", ref v, IntPtr.Zero) >= 0) dp = (v as string) ?? "";
                }
                bool porRuta = ruta.Length > 0 && dp.Equals(ruta, StringComparison.OrdinalIgnoreCase);
                bool igual = nombre.Length > 0 && fn.Equals(nombre, StringComparison.OrdinalIgnoreCase);
                if (porRuta || (igual && porNombre == null))
                {
                    Guid iid = IidFilter;
                    IntPtr pf;
                    if (m.BindToObject(IntPtr.Zero, IntPtr.Zero, ref iid, out pf) >= 0 && pf != IntPtr.Zero)
                    {
                        IBaseFilterDs f = (IBaseFilterDs)Marshal.GetObjectForIUnknown(pf);
                        Marshal.Release(pf);
                        if (porRuta) return f;
                        porNombre = f;
                    }
                }
                if (ruta.Length == 0 && porNombre != null) return porNombre;
            }
            return porNombre;
        }

        // El pin de salida cuyo primer formato es de video (la GC573 tiene ademas dos de audio: no se tocan)
        static IPinDs PinVideo(IBaseFilterDs f)
        {
            IntPtr pe;
            if (f.EnumPins(out pe) < 0 || pe == IntPtr.Zero) return null;
            IEnumPinsDs en = (IEnumPinsDs)Marshal.GetObjectForIUnknown(pe);
            Marshal.Release(pe);
            while (true)
            {
                IntPtr pp; uint got;
                if (en.Next(1, out pp, out got) != 0 || got == 0 || pp == IntPtr.Zero) return null;
                IPinDs pin = (IPinDs)Marshal.GetObjectForIUnknown(pp);
                Marshal.Release(pp);
                int dir;
                IAMStreamConfigDs sc = pin as IAMStreamConfigDs;
                if (pin.QueryDirection(out dir) < 0 || dir != 1 || sc == null) continue;
                int n, sz;
                if (sc.GetNumberOfCapabilities(out n, out sz) < 0 || n <= 0) continue;
                IntPtr caps = Marshal.AllocHGlobal(Math.Max(sz, 256)), pmt;
                try
                {
                    if (sc.GetStreamCaps(0, out pmt, caps) < 0 || pmt == IntPtr.Zero) continue;
                    AmMediaType mt = (AmMediaType)Marshal.PtrToStructure(pmt, typeof(AmMediaType));
                    Borrar(pmt);
                    if (mt.majortype == Video) return pin;
                }
                finally { Marshal.FreeHGlobal(caps); }
            }
        }

        static IPinDs Pin(IBaseFilterDs f, int dir)
        {
            IntPtr pe;
            if (f.EnumPins(out pe) < 0 || pe == IntPtr.Zero) return null;
            IEnumPinsDs en = (IEnumPinsDs)Marshal.GetObjectForIUnknown(pe);
            Marshal.Release(pe);
            while (true)
            {
                IntPtr pp; uint got;
                if (en.Next(1, out pp, out got) != 0 || got == 0 || pp == IntPtr.Zero) return null;
                IPinDs pin = (IPinDs)Marshal.GetObjectForIUnknown(pp);
                Marshal.Release(pp);
                int d;
                if (pin.QueryDirection(out d) >= 0 && d == dir) return pin;
            }
        }

        // 1280x720 (la de la sonda de mpv: la GC573 lo da a 50-60) en YUY2 (o NV12 si el ini dice nv12), a 59,94 si puede; si no, el
        // YUY2 / NV12 mas pequeno de al menos 640x360. El AM_MEDIA_TYPE elegido (con su intervalo) o IntPtr.Zero
        static IntPtr Formato(IAMStreamConfigDs sc, string formato, out int w, out int h, out int stride, out bool nv12)
        {
            w = h = stride = 0; nv12 = false;
            int n, sz;
            if (sc.GetNumberOfCapabilities(out n, out sz) < 0 || n <= 0) return IntPtr.Zero;
            bool quiereNv12 = formato.Trim().Equals("nv12", StringComparison.OrdinalIgnoreCase);
            IntPtr caps = Marshal.AllocHGlobal(Math.Max(sz, 256)), mejor = IntPtr.Zero;
            long mejorNota = long.MaxValue;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    IntPtr pmt;
                    if (sc.GetStreamCaps(i, out pmt, caps) < 0 || pmt == IntPtr.Zero) continue;
                    AmMediaType mt = (AmMediaType)Marshal.PtrToStructure(pmt, typeof(AmMediaType));
                    bool esNv12 = mt.subtype == Nv12, esYuy2 = mt.subtype == Yuy2;
                    int bmi = mt.formattype == VideoInfo && mt.cbFormat >= 88 ? 48 : mt.formattype == VideoInfo2 && mt.cbFormat >= 112 ? 72 : -1;
                    if ((!esNv12 && !esYuy2) || bmi < 0 || mt.pbFormat == IntPtr.Zero) { Borrar(pmt); continue; }
                    int fw = Marshal.ReadInt32(mt.pbFormat, bmi + 4), fh = Math.Abs(Marshal.ReadInt32(mt.pbFormat, bmi + 8));
                    if (fw < 640 || fh < 360) { Borrar(pmt); continue; }
                    long mn = sz >= 128 ? Marshal.ReadInt64(caps, 104) : 0, mx = sz >= 128 ? Marshal.ReadInt64(caps, 112) : 0;
                    bool vale5994 = mn <= 0 || (166833 >= mn - 2 && (mx <= 0 || 166833 <= mx + 2));
                    long nota = (fw == 1280 && fh == 720 ? 0 : 1000000000L + (long)fw * fh) + (esNv12 == quiereNv12 ? 0 : 1000) + (vale5994 ? 0 : 10);
                    if (nota < mejorNota)
                    {
                        Borrar(mejor);
                        mejor = pmt; mejorNota = nota;
                        w = fw; h = fh; nv12 = esNv12;
                        stride = esNv12 ? fw : fw * 2;
                        if (vale5994) Marshal.WriteInt64(mt.pbFormat, 40, 166833);   // AvgTimePerFrame (VIH y VIH2), 59,94
                    }
                    else Borrar(pmt);
                }
            }
            finally { Marshal.FreeHGlobal(caps); }
            return mejor;
        }

        static void Borrar(IntPtr pmt)
        {
            if (pmt == IntPtr.Zero) return;
            try
            {
                AmMediaType mt = (AmMediaType)Marshal.PtrToStructure(pmt, typeof(AmMediaType));
                if (mt.pbFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(mt.pbFormat);
                if (mt.pUnk != IntPtr.Zero) Marshal.Release(mt.pUnk);
                Marshal.FreeCoTaskMem(pmt);
            }
            catch { }
        }

        // ------------------------------------------------------------------ el criterio de ce_standby.lua
        // "nosig" (la pantalla "No Signal" de la capturadora: franjas negras arriba y abajo y un recuadro gris en el centro), "negro" o
        // "imagen". YUY2 / NV12 a RGB como mpv (BT.709, rango limitado)
        public static string Clasificar(IntPtr buf, int len, int w, int h, int stride, bool nv12)
        {
            const int gw = 48, gh = 27;
            int blkN = 0, blkOk = 0, boxN = 0, boxOk = 0;
            double maxl = 0;
            for (int j = 0; j < gh; j++)
            {
                double fy = (j + 0.5) / gh;
                int y = (int)Math.Floor(fy * h);
                for (int i = 0; i < gw; i++)
                {
                    double fx = (i + 0.5) / gw;
                    int x = (int)Math.Floor(fx * w);
                    int Y, U, V;
                    if (nv12)
                    {
                        int py = y * stride + x, pc = h * stride + (y / 2) * stride + (x & ~1);
                        if (pc + 1 >= len) continue;
                        Y = Marshal.ReadByte(buf, py); U = Marshal.ReadByte(buf, pc); V = Marshal.ReadByte(buf, pc + 1);
                    }
                    else
                    {
                        int p = y * stride + (x & ~1) * 2;
                        if (p + 3 >= len) continue;
                        Y = Marshal.ReadByte(buf, p + ((x & 1) != 0 ? 2 : 0)); U = Marshal.ReadByte(buf, p + 1); V = Marshal.ReadByte(buf, p + 3);
                    }
                    double R, G, B;
                    ARgb(Y, U, V, out R, out G, out B);
                    double l = (54 * R + 183 * G + 19 * B) / 256;
                    if (l > maxl) maxl = l;
                    if (fy < 0.27 || fy > 0.67) { blkN++; if (l < 20) blkOk++; }
                    else if (fy > 0.33 && fy < 0.63 && fx > 0.10 && fx < 0.90)
                    {
                        boxN++;
                        if (l > 110 && l < 235 && Math.Abs(R - G) < 25 && Math.Abs(G - B) < 25) boxOk++;
                    }
                }
            }
            if (blkN > 0 && boxN > 0 && blkOk >= blkN * 0.95 && boxOk >= boxN * 0.75) return "nosig";
            if (maxl < 12) return "negro";
            return "imagen";
        }

        static void ARgb(int Y, int U, int V, out double R, out double G, out double B)
        {
            double yy = (Y - 16) * 255.0 / 219.0, u = (U - 128) * 255.0 / 224.0, v = (V - 128) * 255.0 / 224.0;
            R = Math.Max(0, Math.Min(255, yy + 1.5748 * v));
            G = Math.Max(0, Math.Min(255, yy - 0.1873 * u - 0.4681 * v));
            B = Math.Max(0, Math.Min(255, yy + 1.8556 * u));
        }

        // ------------------------------------------------------------------ --ce-sonda-prueba=<carpeta>[;<informe>]
        // Las capturas guardadas (PNG / JPG) por el mismo camino que la sonda: RGB -> YUY2 (BT.709 limitado) -> Clasificar. Para ver que
        // la consola (cualquier imagen suya) sale "imagen" y la pantalla "No Signal" sale "nosig"
        public static int Prueba(string arg)
        {
            string[] p = arg.Split(new char[] { ';' }, 2);
            string dir = p[0], informe = p.Length > 1 ? p[1] : Path.Combine(Paths.Datos, "sonda_prueba.txt");
            StringBuilder sb = new StringBuilder();
            Dictionary<string, int> cuenta = new Dictionary<string, int>();
            List<string> ficheros = new List<string>();
            foreach (string ext in new string[] { "*.png", "*.jpg" }) ficheros.AddRange(Directory.GetFiles(dir, ext, SearchOption.AllDirectories));
            ficheros.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in ficheros)
            {
                string q;
                try
                {
                    using (System.Drawing.Bitmap b0 = new System.Drawing.Bitmap(f))
                    using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(b0, 1280, 720))
                    {
                        int w = 1280, h = 720, stride = w * 2;
                        byte[] yuy2 = new byte[stride * h];
                        System.Drawing.Imaging.BitmapData d = b.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                        byte[] rgb = new byte[d.Stride * h];
                        Marshal.Copy(d.Scan0, rgb, 0, rgb.Length);
                        int ds = d.Stride;
                        b.UnlockBits(d);
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x += 2)
                            {
                                int o1 = y * ds + x * 4, o2 = o1 + 4;
                                int y1, u1, v1, y2, u2, v2;
                                AYuv(rgb[o1 + 2], rgb[o1 + 1], rgb[o1], out y1, out u1, out v1);
                                AYuv(rgb[o2 + 2], rgb[o2 + 1], rgb[o2], out y2, out u2, out v2);
                                int o = y * stride + x * 2;
                                yuy2[o] = (byte)y1; yuy2[o + 1] = (byte)((u1 + u2) / 2); yuy2[o + 2] = (byte)y2; yuy2[o + 3] = (byte)((v1 + v2) / 2);
                            }
                        GCHandle hnd = GCHandle.Alloc(yuy2, GCHandleType.Pinned);
                        try { q = Clasificar(hnd.AddrOfPinnedObject(), yuy2.Length, w, h, stride, false); }
                        finally { hnd.Free(); }
                    }
                }
                catch (Exception ex) { q = "error (" + ex.Message + ")"; }
                int c; cuenta.TryGetValue(q, out c); cuenta[q] = c + 1;
                sb.Append(q.PadRight(8)).Append(' ').Append(f).Append("\r\n");
            }
            StringBuilder res = new StringBuilder("Sonda de la espera, prueba con " + ficheros.Count + " imagenes de " + dir + ":");
            foreach (KeyValuePair<string, int> k in cuenta) res.Append(' ').Append(k.Key).Append('=').Append(k.Value);
            File.WriteAllText(informe, res + "\r\n" + sb, new UTF8Encoding(false));
            Log.Write(res.ToString() + " (" + informe + ")");
            return 0;
        }

        static void AYuv(int R, int G, int B, out int Y, out int U, out int V)
        {
            double y = 0.2126 * R + 0.7152 * G + 0.0722 * B;
            Y = Clamp((int)Math.Round(16 + y * 219.0 / 255.0));
            U = Clamp((int)Math.Round(128 + (B - y) / 1.8556 * 224.0 / 255.0));
            V = Clamp((int)Math.Round(128 + (R - y) / 1.5748 * 224.0 / 255.0));
        }

        static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }
    }
}
