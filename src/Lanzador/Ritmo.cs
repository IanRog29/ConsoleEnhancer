// Ritmo.cs - de donde vienen los tirones del camino mpv + Magpie (22-11-2026, 1.2.2, agente RITMO)
//
// El usuario: "hay microstutters (puede que lo causen vrr...)". El log decia "Tirones sueltos: Magpie estuvo 60-130 ms sin imagen nueva" pero
// no de donde venian. Ahora cada tiron se reparte entre:
//   juego        la imagen ya llego tarde a mpv (la marca de tiempo de la capturadora salta: la consola saco una imagen de 50 ms, o la
//                capturadora perdio una). Es del juego, no se puede arreglar en el PC
//   captura_app  mpv la saco tarde justo despues de una captura de la propia app (la que mira si se ha vuelto al menu de la consola:
//                screenshot-raw para el hilo de mpv; medido el 22-11 sin grafica: 16 ms a 1080p, 22 ms a 1440p, 44-58 ms a 4K, mas que el
//                hueco de 33 ms entre dos imagenes del modo 30)
//   mpv          la marca de la capturadora no salta y no hubo captura, pero mpv la saco tarde (CPU ocupada)
//   wgc          mpv la saco a tiempo y Magpie la recibio tarde o no la recibio (la captura de Windows entre mpv y Magpie: Magpie se queda
//                con la mas nueva y tira las demas mientras esta ocupado)
//   grafica      lo anterior con un pico de la grafica en Magpie (la espera de su ultima imagen > 2 veces la media y 15 ms mas)
//   salida       Magpie recibio bien las imagenes pero presento con un hueco (su ritmo, la generacion o la cola de la pantalla)
// Lo de mpv llega al momento (scripts\ce_diag.lua manda "switch2-diag-hueco" con cada imagen que sale 8 ms o mas tarde: hora, marca y lo
// normal; scripts\ce_power.lua "switch2-diag-captura" con lo que paro a mpv cada captura); lo de Magpie, de su log (las lineas "Output
// cadence" cada ~4 s, con su hora, y "DLSSFG frontend timing": lo que esperan sus imagenes en su cola). Cada 5 min, el resumen.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ConsoleEnhancer
{
    // ---- La captura de la app por fichero (22-11, 1.2.2, segunda parte de RITMO). Las capturas que miran el menu de la consola eran un
    // screenshot-raw de mpv: pasa la imagen a RGB y la copia con el nucleo de mpv parado (4K: 52-56 ms, peor 68-80; mas que los 33 ms entre
    // dos imagenes del modo 30). Ahora, jugando con Magpie a 4K / 1440p, un screenshot-to-file (PNG sin comprimir, scripts\ce_power.lua):
    // el nucleo solo se para mientras coge la imagen (18-19 ms, peor 24-32) y el RGB, el PNG y el fichero van en otro hilo de mpv. Aqui se
    // lee el PNG (sin GDI+: sin comprimir es copiar), se hace la ligera de ~1280x720 igual que light_frame de Lua, se escribe el .raw de
    // siempre (bgr0) para AnalyzeGrab y se le devuelve a mpv la rejilla de 48x27 del negro / "No Signal". Medido sin grafica (mpv 0.42):
    // el PNG sale identico pixel a pixel a screenshot-raw (JPG no: hasta 104 de diferencia) y tarda en estar 144-190 ms a 4K (64-77 a
    // 1440p) frente a 63-100: la deteccion del menu tarda ~0,1 s mas por captura. captura_png=0 = screenshot-raw como antes
    partial class Config
    {
        public bool GrabPng = true;
        // captura_png=1 (de serie): todas las capturas de la app por PNG a la tuberia (tambien en el menu de la consola: con screenshot-raw
        // la ligera de 3,7 MB iba al disco cada 0,5 s, ~26 GB por hora en el menu a 4K; el PNG por la tuberia tarda casi lo mismo: 105-150
        // ms a 4K frente a 90-110); captura_png=juego: solo jugando con Magpie; captura_png=0: screenshot-raw como antes. La imagen
        // congelada del telon sigue siempre con screenshot-raw
        public bool GrabPngTodas = true;
    }

    partial class TrayApp
    {
        // Por PNG: jugando (Magpie ya escala, fuera del menu y sin estar confirmandolo, que pide capturas cada 0,25 s y quiere la respuesta
        // enseguida) y por mpv (el motor contesta las capturas el mismo). (22-11, tercera parte: tambien a 1080p, por la tuberia no cuesta
        // disco y con la imagen quieta el nucleo de mpv no se para nada)
        bool UsaCapturaPng()
        {
            return cfg.GrabPng && !Config.EngineActive && (cfg.GrabPngTodas || (lsReady && !InHome && !menuMaq.Confirmando));
        }

        // ---- 22-11 (tercera parte, el coordinador: "25 MB cada 1,5 s son ~60 GB por hora de juego escritos en el SSD"): el PNG no va a
        // un fichero sino a una tuberia con nombre que abre la app (\\.\pipe\ce-grab-<pid>-<n>.png: mpv la abre como un fichero y elige el
        // formato por la extension). La app lee el PNG de la tuberia mientras mpv lo escribe y la ligera pasa a AnalyzeGrab en memoria:
        // NADA al disco (antes, con screenshot-raw, el .raw de la ligera: 3,7 MB por captura, ~9 GB/hora a 4K cada 1,5 s y ~12 GB/hora a
        // 1080p cada 0,6 s). Sin compresion: con compresion 1 el PNG de una imagen de juego de verdad a 4K tarda 522 ms en estar (13,4 MB)
        // frente a 113 ms (25 MB); a 1440p 247 frente a 63. Si la tuberia no se puede crear, el PNG va a %TEMP%\switch2_grab.raw.png (un
        // solo fichero, se sobrescribe y se borra al leerlo)
        int pngN; bool rejillaDicha;
        [ThreadStatic] static byte[] grabEnMemoria;   // (AnalyzeGrab: la ligera ya en memoria, sin leer el .raw)

        // La ruta para mpv: la tuberia (con su lector ya esperando) o, si no se puede, el fichero de siempre
        string DestinoCapturaPng(bool light)
        {
            try
            {
                string name = "ce-grab-" + System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "-" + (++pngN).ToString(CultureInfo.InvariantCulture) + ".png";
                System.IO.Pipes.NamedPipeServerStream ps = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.In, 1,
                    System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous, 1 << 22, 1 << 12);
                DateTime pedida = lastGrab;
                Thread t = new Thread(delegate () { LeeTuberiaPng(ps, light, pedida); });
                t.IsBackground = true;
                t.Priority = ThreadPriority.BelowNormal;
                t.Start();
                return @"\\.\pipe\" + name;
            }
            catch (Exception ex) { Log.Error("Captura por PNG: no se pudo crear la tuberia (va por fichero)", ex); return GrabPath + ".png"; }
        }

        void LeeTuberiaPng(System.IO.Pipes.NamedPipeServerStream ps, bool light, DateTime pedida)
        {
            bool analizada = false;
            try
            {
                IAsyncResult ar = ps.BeginWaitForConnection(null, null);
                if (!ar.AsyncWaitHandle.WaitOne(4000)) return;   // (mpv no la abrio: ya contesto "switch2-grabbed" vacio)
                ps.EndWaitForConnection(ar);
                // (con un bufer de 1 MB: leyendo a trozos de 8 KB la tuberia tardaba 520 ms a 4K; asi 128 ms, lo mismo que por fichero)
                PngCaptura c;
                using (BufferedStream bs = new BufferedStream(ps, 1 << 20))
                {
                    c = PngCaptura.LeeFlujo(bs, light, 48, 27);   // (la rejilla de ce_power.lua: grid_w / grid_h de serie)
                    try { byte[] resto = new byte[1 << 16]; while (bs.Read(resto, 0, resto.Length) > 0) { } } catch { }   // (que mpv acabe de escribir)
                }
                analizada = UsaPng(c, null, light);
            }
            catch (Exception ex) { Log.Error("Captura por PNG (tuberia)", ex); }
            finally
            {
                try { ps.Dispose(); } catch { }
                if (!analizada && lastGrab == pedida) grabBusy = false;
            }
        }

        // La imagen leida: la rejilla a mpv y la ligera a AnalyzeGrab (en memoria). false = no se pudo
        int pngFallos;
        bool UsaPng(PngCaptura c, string origen, bool light)
        {
            if (c == null)
            {
                // (3 seguidas sin poder leerla: el resto de la sesion como antes, con screenshot-raw)
                if (++pngFallos >= 3 && cfg.GrabPng) { cfg.GrabPng = false; Log.Write("Captura por PNG: no se puede leer el PNG de mpv (" + PngCaptura.Ultimo + "): se vuelve a screenshot-raw en esta sesion"); }
                return false;
            }
            pngFallos = 0;
            lock (cmdQueue) cmdQueue.Add(JsonCmd("script-message", "switch2-grab-sample", c.Avg.ToString("0.###", CultureInfo.InvariantCulture),
                                                 c.MaxL.ToString("0.###", CultureInfo.InvariantCulture), c.HuellaHex));
            grabEnMemoria = c.Bgr0;
            AnalyzeGrab(GrabPath, c.W, c.H, c.W * 4, "bgr0");   // (pone grabBusy=false al acabar)
            return true;
        }

        // "switch2-grabbed-png","<raw>","<png>","<modo>","<rejilla w>","<rejilla h>","<ms hasta el PNG>": con la tuberia, solo el tiempo
        // (la imagen ya la lee LeeTuberiaPng); con el fichero, se lee aqui
        void CapturaPngLista(string line)
        {
            Match m = Regex.Match(line, "switch2-grabbed-png\",\"((?:[^\"\\\\]|\\\\.)+)\",\"((?:[^\"\\\\]|\\\\.)+)\",\"([^\"]*)\",\"(\\d+)\",\"(\\d+)\",\"([0-9.]+)\"");
            if (!m.Success) { grabBusy = false; return; }
            string png = m.Groups[2].Value.Replace("\\\\", "\\");
            bool light = m.Groups[3].Value.StartsWith("light", StringComparison.Ordinal);
            int gw = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture), gh = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            RitmoDiag.PngListo(double.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture));
            if (png.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
            {
                // (la tuberia se lee con la rejilla de 48x27 de ce_power.lua; si alguna vez cambia, la huella no casaria: se avisa una vez)
                if ((gw != 48 || gh != 27) && !rejillaDicha) { rejillaDicha = true; Log.Write("Captura por PNG: la rejilla de mpv es " + gw + "x" + gh + " y no 48x27: el negro / No Signal de las capturas por PNG no valen"); }
                return;
            }
            Thread t = new Thread(delegate () { CapturaPngProcesa(png, light, gw, gh); });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        void CapturaPngProcesa(string png, bool light, int gw, int gh)
        {
            bool analizada = false;
            try { analizada = UsaPng(PngCaptura.Lee(png, light, gw, gh), png, light); }
            catch (Exception ex) { Log.Error("Captura por PNG (fichero)", ex); }
            finally
            {
                if (!analizada) grabBusy = false;
                try { File.Delete(png); } catch { }
            }
        }
    }

    // PNG de 8 bits RGB / RGBA sin entrelazar (el de mpv con --screenshot-high-bit-depth=no), leido fila a fila: sin la imagen entera en
    // memoria. Saca la captura en bgr0 (la ligera: 1 de cada step pixeles y filas, step = redondeo de ancho / 1280, como light_frame de
    // ce_power.lua) y la rejilla de sample_raw (luma media / maxima y la huella R,G,B de gw x gh puntos de la imagen entera). Si el PNG
    // es de otra forma, con GDI+
    sealed class PngCaptura
    {
        public int W, H;
        public byte[] Bgr0;
        public double Avg, MaxL;
        public string HuellaHex;
        public static string Ultimo = "";

        static int BE32(byte[] b, int o) { return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]; }

        // Los datos de los IDAT seguidos, como un flujo (sin copiarlos)
        sealed class Idat : Stream
        {
            readonly Stream f; int left; bool fin;
            public Idat(Stream f, int first) { this.f = f; left = first; }
            public override int Read(byte[] buf, int off, int n)
            {
                while (left == 0 && !fin)
                {
                    byte[] h = new byte[12];
                    if (ReadFull(f, h, 0, 4) < 4) { fin = true; break; }   // (el CRC del anterior)
                    if (ReadFull(f, h, 4, 8) < 8) { fin = true; break; }
                    int len = BE32(h, 4);
                    string type = Encoding.ASCII.GetString(h, 8, 4);
                    if (type != "IDAT" || len < 0) { fin = true; break; }
                    left = len;
                }
                if (left == 0) return 0;
                int r = f.Read(buf, off, Math.Min(n, left));
                if (r <= 0) { fin = true; return 0; }
                left -= r;
                return r;
            }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); }
            public override void SetLength(long v) { throw new NotSupportedException(); }
            public override void Write(byte[] b, int o, int n) { throw new NotSupportedException(); }
        }

        static int ReadFull(Stream s, byte[] b, int off, int n)
        {
            int t = 0;
            while (t < n) { int r = s.Read(b, off + t, n - t); if (r <= 0) break; t += r; }
            return t;
        }

        // Quita el filtro de una fila (0 nada, 1 Sub, 2 Up, 3 Average, 4 Paeth); row[0] = el tipo, los datos desde row[1]
        static void Unfilter(byte[] row, byte[] prev, int bpp, int len)
        {
            int ft = row[0];
            if (ft == 0) return;
            for (int i = 0; i < len; i++)
            {
                int a = i >= bpp ? row[1 + i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0, x = row[1 + i];
                int v;
                switch (ft)
                {
                    case 1: v = x + a; break;
                    case 2: v = x + b; break;
                    case 3: v = x + ((a + b) >> 1); break;
                    case 4:
                        {
                            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                            v = x + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                            break;
                        }
                    default: v = x; break;
                }
                row[1 + i] = (byte)v;
            }
        }

        public static PngCaptura Lee(string file, bool light, int gw, int gh)
        {
            try
            {
                PngCaptura c = Rapido(file, light, gw, gh);
                if (c != null) return c;
                return ConGdi(file, light, gw, gh);
            }
            catch (Exception ex) { Ultimo = ex.Message; return null; }
        }

        static int Paso(int w) { return Math.Max(1, (int)Math.Floor(w / 1280.0 + 0.5)); }

        sealed class Salida
        {
            public int W, H, Step, Gw, Gh; public byte[] Out; public double Sum, MaxL; public int N; public byte[] Fp;
            public int[] GridX; public int[] GridYRow;   // por fila de la imagen: el indice j de la rejilla o -1
            public Salida(int w, int h, bool light, int gw, int gh)
            {
                W = w; H = h; Gw = gw; Gh = gh; Step = light ? Paso(w) : 1;
                int ow = w / Step, oh = h / Step;
                Out = new byte[ow * oh * 4];
                Fp = new byte[gw * gh * 3];
                GridX = new int[gw];
                for (int i = 0; i < gw; i++) GridX[i] = (int)Math.Floor((i + 0.5) * w / gw);
                GridYRow = new int[h];
                for (int y = 0; y < h; y++) GridYRow[y] = -1;
                for (int j = 0; j < gh; j++) { int y = (int)Math.Floor((j + 0.5) * h / gh); if (y >= 0 && y < h) GridYRow[y] = j; }
            }
            public int OW { get { return W / Step; } }
            public int OH { get { return H / Step; } }
            // Una fila de la imagen entera: px = R,G,B[,A] desde off, bpp bytes por pixel
            public void Fila(int y, byte[] px, int off, int bpp)
            {
                if (y % Step == 0 && y / Step < OH)
                {
                    int ow = OW, o = (y / Step) * ow * 4;
                    for (int x = 0; x < ow; x++)
                    {
                        int s = off + x * Step * bpp;
                        Out[o] = px[s + 2]; Out[o + 1] = px[s + 1]; Out[o + 2] = px[s]; Out[o + 3] = 0;
                        o += 4;
                    }
                }
                int j = GridYRow[y];
                if (j >= 0)
                    for (int i = 0; i < Gw; i++)
                    {
                        int s = off + GridX[i] * bpp;
                        int r = px[s], g = px[s + 1], b = px[s + 2];
                        double l = (54 * r + 183 * g + 19 * b) / 256.0;
                        Sum += l; N++; if (l > MaxL) MaxL = l;
                        int k = (j * Gw + i) * 3;
                        Fp[k] = (byte)r; Fp[k + 1] = (byte)g; Fp[k + 2] = (byte)b;
                    }
            }
            public PngCaptura Fin()
            {
                PngCaptura c = new PngCaptura();
                c.W = OW; c.H = OH; c.Bgr0 = Out;
                c.Avg = N > 0 ? Sum / N : 0; c.MaxL = MaxL;
                StringBuilder sb = new StringBuilder(Fp.Length * 2);
                foreach (byte b in Fp) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                c.HuellaHex = sb.ToString();
                return c;
            }
        }

        static PngCaptura Rapido(string file, bool light, int gw, int gh)
        {
            using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20)) return Rapido(f, light, gw, gh);
        }

        // De un flujo que solo se lee hacia delante (la tuberia de mpv): sin la imagen entera en memoria. null = no se pudo (no hay GDI+:
        // el flujo ya no se puede volver a leer)
        public static PngCaptura LeeFlujo(Stream s, bool light, int gw, int gh)
        {
            try { return Rapido(s, light, gw, gh); }
            catch (Exception ex) { Ultimo = ex.Message; return null; }
        }

        static PngCaptura Rapido(Stream f, bool light, int gw, int gh)
        {
            {
                byte[] h = new byte[8];
                if (ReadFull(f, h, 0, 8) < 8 || h[0] != 0x89 || h[1] != (byte)'P' || h[2] != (byte)'N' || h[3] != (byte)'G') { Ultimo = "no es PNG"; return null; }
                int w = 0, hh = 0, depth = 0, ct = 0, inter = 0;
                byte[] ch = new byte[8];
                while (true)
                {
                    if (ReadFull(f, ch, 0, 8) < 8) { Ultimo = "sin IDAT"; return null; }
                    int len = BE32(ch, 0);
                    string type = Encoding.ASCII.GetString(ch, 4, 4);
                    if (type == "IDAT")
                    {
                        if (depth != 8 || (ct != 2 && ct != 6) || inter != 0 || w <= 0 || hh <= 0) { Ultimo = "PNG de " + depth + " bits, tipo " + ct; return null; }
                        int bpp = ct == 2 ? 3 : 4, rowLen = w * bpp;
                        Salida s = new Salida(w, hh, light, gw, gh);
                        using (Idat id = new Idat(f, len))
                        {
                            byte[] zh = new byte[2];
                            if (ReadFull(id, zh, 0, 2) < 2) { Ultimo = "zlib corto"; return null; }
                            using (DeflateStream ds = new DeflateStream(id, CompressionMode.Decompress))
                            {
                                byte[] row = new byte[rowLen + 1], prev = new byte[rowLen];
                                for (int y = 0; y < hh; y++)
                                {
                                    if (ReadFull(ds, row, 0, rowLen + 1) < rowLen + 1) { Ultimo = "faltan filas (" + y + " de " + hh + ")"; return null; }
                                    Unfilter(row, prev, bpp, rowLen);
                                    s.Fila(y, row, 1, bpp);
                                    Buffer.BlockCopy(row, 1, prev, 0, rowLen);
                                }
                            }
                        }
                        return s.Fin();
                    }
                    byte[] d = new byte[Math.Max(0, len)];
                    if (ReadFull(f, d, 0, d.Length) < d.Length) { Ultimo = "PNG cortado"; return null; }
                    if (type == "IHDR" && len >= 13) { w = BE32(d, 0); hh = BE32(d, 4); depth = d[8]; ct = d[9]; inter = d[12]; }
                    if (type == "IEND") { Ultimo = "sin IDAT"; return null; }
                    if (ReadFull(f, ch, 0, 4) < 4) { Ultimo = "PNG cortado"; return null; }   // (CRC)
                }
            }
        }

        static PngCaptura ConGdi(string file, bool light, int gw, int gh)
        {
            using (Bitmap b = new Bitmap(file))
            {
                Rectangle r = new Rectangle(0, 0, b.Width, b.Height);
                BitmapData d = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    Salida s = new Salida(b.Width, b.Height, light, gw, gh);
                    byte[] row = new byte[b.Width * 4], rgb = new byte[b.Width * 3];
                    for (int y = 0; y < b.Height; y++)
                    {
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                        for (int x = 0, o = 0; x < b.Width; x++, o += 3) { rgb[o] = row[x * 4 + 2]; rgb[o + 1] = row[x * 4 + 1]; rgb[o + 2] = row[x * 4]; }
                        s.Fila(y, rgb, 0, 3);
                    }
                    return s.Fin();
                }
                finally { b.UnlockBits(d); }
            }
        }
    }

    // ---- Como presenta Magpie cada modo (22-11, 1.2.2; 1.2.3 RITMO2: DLSS FG x2 a refresco fijo como XeSS FG). De serie:
    //   modo 30 sin generacion      "cada 2 refrescos" por DXGI a 120 Hz (1.2.3)     magpie_fijo_30=0: DirectComposition (1.2.2); magpie_vrr_30=1: VRR
    //   modo 30, DLSS FG x2         refresco fijo "cada 2 refrescos" (1.2.3)          magpie_fijo_30_dlss=0: por VRR (lo de la 1.2.2)
    //                               (solo con la pantalla a 120 Hz; a 119,88 / 144 / 165 Hz o si el driver no deja, por VRR como antes)
    //   modo 30, XeSS/FSR 3/VFG x2  refresco fijo "cada 2 refrescos" (dxgi_30_*)     magpie_vrr_30_otras=1: por VRR
    //   modo 30, cualquiera x3 / x4 VRR (con "cada 2 refrescos" la salida no pasaria de 60: 89,9 y 119,88 imagenes/s no caben); sin VRR,
    //                               refresco fijo con la sincronizacion forzada (cada imagen 1 refresco)
    //   modo 60 (sin / DLSS FG)     como antes (DXGI con la sincronizacion forzada / VRR)
    // Y el ritmo del contenido de Magpie con generacion: magpie_ritmo_margen_fg=N (%: contentRate un N % por encima del de la captura; el
    // DLSS FG de Magpie no deja salir mas despacio que contentRate x multiplicador ni meter imagenes reales mas deprisa que contentRate:
    // con 29,97 justos no puede vaciar su cola tras un tiron) y magpie_sigue_fuente_30_fg=1 (contentMode "seguir la fuente": sin ese suelo
    // ni la espera de cada imagen real). De serie 0 / 0 (lo de antes) hasta medirlo con la tele
    partial class Config
    {
        // (1.2.3 RITMO2) RitmoFijo30Dlss de serie: medido el 22-11 (tele Panasonic 3840x2160@120 con VRR, fuente con la cadencia de la PS5
        // -30 fps en 59,94 y una imagen de 50 ms cada 2 s-, 4K, DLSS 5 nivel 2 al 45-50 %, DLSS FG x2, PresentMon + el log de Magpie):
        //   VRR (1.2.2)  66,6 % de las imagenes vistas a 16,7 +-2 ms, sd 2,76 ms, p99 48,5 ms, 28,6 reales/s de 29,77 (4 % perdidas)
        //   fijo         95,8 %, sd 1,55, p99 33,3, 29,7 reales/s (0,2 % perdidas); la latencia igual: 72,8 ms la real (n=10) frente a 72,3
        //                (56,1 / 56,3 la inventada): con VRR la captura de Windows llega mas tarde (mpv -> WGC 15,4 frente a 12,2 ms) y Magpie
        //                espera mas (WGC -> presentar 47,8 frente a 41,5); con el fijo se espera al hueco de la tele (10,7 frente a 2,3)
        //   XeSS FG x2   fijo 98,5 % y 29,75 reales/s; por VRR 67,6 % y 28,3 reales/s (por eso XeSS ya iba fijo)
        public bool RitmoVrr30 = false, RitmoVrr30Otras = false, RitmoFijo30Dlss = true, RitmoSigue30Fg = false;
        // (1.2.3 RITMO2) magpie_fijo_30 (de serie 1): el 30 SIN generacion por DXGI con la sincronizacion "cada 2 refrescos" (estado 4) a
        // 120 Hz, en vez de DirectComposition. Medido el 22-11 (4K, DLSS 5 al 65 %): por DirectComposition cada imagen se ve 2-12 refrescos
        // (40,8-44 % a 4, sd 8,3-9 ms, p99 67-75 ms) sin perder ninguna, 52-54 ms; por VRR (magpie_vrr_30=1) pierde 0,56 reales/s (la
        // captura de Windows va al ritmo lento de la pantalla); con el fijo, 0 perdidas (29,68 de 29,73), p99 33,5 ms (la tele ve cada 16,7 ms
        // la nueva o su repeticion: 2 refrescos el 89 %, 4 el 7 %), 54,9 ms (n=10), la grafica igual (62 frente a 67 %). Una pasada.
        // magpie_fijo_30=0 = DirectComposition como en la 1.2.2. Solo con la pantalla a 120 Hz
        public bool RitmoFijo30 = true;
        // (1.2.3, REVISION3: sin Reflex, como las otras dos sincronizaciones fijas: con Reflex Magpie presenta sin sincronizacion vertical)
        public bool RitmoFijo30SinFgNow { get { int hz = Pantalla.Hz(); return RitmoFijo30 && !RitmoVrr30SinFgNow && CapMode && FgOff && !HomeNative && !Mode120 && !ReflexNow && !NvProfiles.VsyncBroken && hz >= 118 && hz <= 122; } }
        public double RitmoMargenFg = 0;
        // ---- Las capturas de la app jugando a 4K (las que miran si se ha vuelto al menu de la consola, TrayApp.GrabTick): cada una es un
        // screenshot-raw de mpv que para su hilo mientras pasa la imagen de 4K a RGB y la copia (medido el 22-11 sin grafica, mpv 0.42:
        // 44-80 ms a 4K, 22 ms a 1440p, 16 ms a 1080p). A 4K es mas que el hueco entre dos imagenes del modo 30 (33 ms): cada captura deja
        // la siguiente imagen real 10-45 ms tarde y con la generacion de Magpie (su ritmo sigue a las imagenes que llegan; con VRR la tele
        // va con el) se ve como un tiron; con el refresco fijo de XeSS FG casi no (cae en su hueco de 2 refrescos). Antes una cada 1,5 s;
        // ahora, jugando a 4K con generacion en el modo 30, una cada captura_juego_4k_fg_s (de serie 3: el menu de la consola se ve, como
        // mucho, 1,5 s mas tarde). captura_juego_4k_s = la de los demas casos a 4K (de serie 1,5, como antes)
        // (22-11, segunda parte: con la captura por fichero, captura_png=1 de serie, mpv para 18-19 ms (peor 24-32) a 4K: menos que el
        // hueco del modo 30 y la captura sale justo tras una imagen, asi que no retrasa la siguiente: otra vez cada 1,5 s. Las 3 s solo
        // si se vuelve a screenshot-raw: captura_png=0, o 3 PNG seguidos que no se pudieron leer)
        public double Grab4kSecs = 1.5, Grab4kFgSecs = 3.0;
        public int RitmoResumenSecs = 300;   // ritmo_resumen_s: cada cuanto va al log "Tirones por origen" (de serie 5 min; pruebas: menos)
        // (solo por mpv + Magpie: con el motor propio la captura no pasa por mpv)
        public double RitmoGrab4kSecs { get { return CapMode && !FgOff && !HomeNative && !EngineActive && !GrabPng ? Math.Max(Grab4kSecs, Grab4kFgSecs) : Grab4kSecs; } }

        bool CeParseRitmo(string key, string val)
        {
            string v = val.Trim().ToLowerInvariant();
            bool si = v == "1" || v == "si" || v == "true" || v == "on" || v == "yes";
            switch (key)
            {
                case "magpie_vrr_30": RitmoVrr30 = si; return true;
                case "magpie_vrr_30_otras": RitmoVrr30Otras = si; return true;
                case "magpie_fijo_30_dlss": RitmoFijo30Dlss = si; return true;
                case "magpie_fijo_30": RitmoFijo30 = si; return true;
                case "magpie_sigue_fuente_30_fg": RitmoSigue30Fg = si; return true;
                case "captura_png": GrabPng = si || v == "juego"; GrabPngTodas = si; return true;   // (22-11, 1.2.2: 1 todas, juego, 0 = screenshot-raw como antes)
                case "ritmo_resumen_s":
                    {
                        int n;
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) RitmoResumenSecs = Math.Max(20, Math.Min(3600, n));
                        return true;
                    }
                case "captura_juego_4k_s":
                case "captura_juego_4k_fg_s":
                    {
                        double d;
                        if (!double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return true;
                        d = Math.Max(0.3, Math.Min(10, d));
                        if (key == "captura_juego_4k_s") Grab4kSecs = d; else Grab4kFgSecs = d;
                        return true;
                    }
                case "magpie_ritmo_margen_fg":
                    {
                        double d;
                        if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) RitmoMargenFg = Math.Max(0, Math.Min(10, d));
                        return true;
                    }
            }
            return false;
        }

        // El modo 30 con generacion por VRR (con el VRR encendido, Config.MagpieVrr30Fg): DLSS FG x2 (salvo magpie_fijo_30_dlss), x3 / x4
        // con cualquiera de Magpie, y XeSS / FSR 3 / VFG x2 con magpie_vrr_30_otras. Smooth Motion nunca (va por el driver)
        bool RitmoVrr30FgTipo
        {
            get
            {
                string t = FgTypeNow;
                if (t == "smooth") return false;
                // (1.2.4 ronda 6, QUITAFG: sin DLSS FG -magpie_fijo_30_dlss- ni x3 / x4: XeSS FG / FSR 3 FG x2 por VRR solo con magpie_vrr_30_otras)
                return RitmoVrr30Otras;
            }
        }
        // El modo 30 sin generacion por VRR (magpie_vrr_30=1)
        bool RitmoVrr30SinFgNow { get { return RitmoVrr30 && MagpieVrr30Fg && CapMode && FgOff; } }
        // x3 / x4 sin VRR: DXGI con la sincronizacion vertical forzada (estado 3 de NvProfiles), cada imagen un refresco. (1.2.4 ronda 6,
        // QUITAFG: sin x3 / x4, nunca)
        public bool RitmoFijo1Now { get { return false; } }
        public bool RitmoSigueNow { get { return RitmoSigue30Fg && CapMode && !FgOff && FgTypeNow != "smooth"; } }
        public double RitmoRate(double rate) { return !FgOff && RitmoMargenFg > 0 ? Math.Round(rate * (1 + RitmoMargenFg / 100.0), 2) : rate; }
    }

    static class RitmoDiag
    {
        struct HuecoMpv { public DateTime At; public double Hora, Marca, Normal, NormalMarca; }
        struct Ventana { public DateTime Desde, Hasta; public double CapMax, OutMax, CapP95; public bool Pico, Arranque; }
        struct Captura { public DateTime At; public double Ms; }
        struct Evento { public DateTime At; public string Que; }
        static readonly object sync = new object();
        static readonly List<HuecoMpv> mpv = new List<HuecoMpv>();
        static readonly List<Ventana> ventanas = new List<Ventana>();
        static readonly List<Captura> capturas = new List<Captura>();
        static readonly List<Evento> eventos = new List<Evento>();
        static DateTime ultimaVentana = DateTime.MinValue;
        static int ventanasSesion;
        // (22-11, 1.2.3 RITMO2) "arranque": la primera ventana de 4 s de cada Magpie (se abre la captura de nuevo -la marca de la
        // capturadora salta ~150 ms- y la grafica se pone a ritmo: en el log del usuario del 22-11, 96-158 ms en TODAS las aperturas, y se
        // contaban como "juego" o "wgc"); "ventana": algo de Windows o de la app encima de la imagen en ese momento (el menu de ajustes, el
        // Buscar de Windows... : Windows deja de mostrar a Magpie directamente, compone, y se pierde el ritmo; 10:28:40 y 10:30:04 del log)
        // "cola": la imagen llego a Magpie pero su cola hacia la pantalla estaba llena (su presentacion no llega a soltar sitio: el ritmo
        // que se impone no deja recuperar un retraso) y la captura de Windows, que solo guarda la ultima, la cambio por la siguiente
        // "quieta": la imagen de la consola estaba quieta (menu, pausa, escena fija): el filtro de repetidas de mpv deja pasar una copia cada 3
        // refrescos (50 ms) y Magpie la recibe cada ~50 ms (+ lo que tarde la captura de Windows): no es un tiron ni se nota (la imagen es la
        // misma). Antes, en una escena quieta larga mpv ya no avisaba de nada (lo normal para el ya eran 50 ms) y salia "wgc"
        public static readonly string[] Nombres = { "juego", "captura_app", "mpv", "wgc", "grafica", "salida", "arranque", "ventana", "cola", "quieta" };
        // contadores del resumen (5 min) por origen, y aparte: capturas de la app (cuantas, ms parado mpv) y las imagenes de mpv que
        // salieron tarde justo despues de una (aunque Magpie no viera un tiron: con la generacion y VRR un retraso de 10-25 ms ya se nota)
        static readonly int[] cuenta = new int[10];
        static readonly double[] peor = new double[10];
        struct RitmoMpv { public DateTime At; public bool Copia; }
        static readonly List<RitmoMpv> ritmosMpv = new List<RitmoMpv>();
        struct Anillo { public DateTime At; public double Captadas, Espera; }
        static readonly List<Anillo> anillos = new List<Anillo>();
        static int nCapt, nCaptTarde, nPng; static double sumCapt, maxCapt, sumPng, maxPng;
        static readonly List<double> fifo = new List<double>(), espera = new List<double>();
        static readonly List<DateTime> tardeSueltas = new List<DateTime>();

        static double Num(string s)
        {
            double v; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        // "switch2-diag-hueco","<hora ms> <marca ms> <normal ms> [<normal de la marca ms>]" (scripts\ce_diag.lua)
        public static void MpvHueco(string txt)
        {
            string[] p = (txt ?? "").Split(' ');
            if (p.Length < 3) return;
            HuecoMpv h = new HuecoMpv();
            h.At = DateTime.Now; h.Hora = Num(p[0]); h.Marca = Num(p[1]); h.Normal = Num(p[2]); h.NormalMarca = p.Length > 3 ? Num(p[3]) : h.Normal;
            if (double.IsNaN(h.Hora) || double.IsNaN(h.Marca) || double.IsNaN(h.Normal)) return;
            if (double.IsNaN(h.NormalMarca)) h.NormalMarca = h.Normal;
            lock (sync)
            {
                mpv.Add(h);
                while (mpv.Count > 0 && (h.At - mpv[0].At).TotalSeconds > 60) mpv.RemoveAt(0);
                // tarde por la hora (no por la marca) justo despues de una captura de la app
                if (h.Hora - h.Normal >= 8 && h.Marca - h.NormalMarca < 8)
                {
                    if (TrasCaptura(h.At)) nCaptTarde++;
                    else { tardeSueltas.Add(h.At); while (tardeSueltas.Count > 0 && (h.At - tardeSueltas[0]).TotalSeconds > 2) tardeSueltas.RemoveAt(0); }
                }
            }
        }

        // "switch2-diag-captura","<ms>" (scripts\ce_power.lua: lo que paro a mpv una captura de la app)
        public static void MpvCaptura(string txt)
        {
            // ("png": la captura por fichero; lo que para a mpv no se ve desde Lua, solo cuando empieza)
            bool png = (txt ?? "").Trim() == "png";
            double ms = png ? 0 : Num((txt ?? "").Trim());
            if (double.IsNaN(ms)) return;
            Captura c = new Captura(); c.At = DateTime.Now.AddMilliseconds(-ms); c.Ms = png ? -1 : ms;
            lock (sync)
            {
                capturas.Add(c);
                while (capturas.Count > 0 && (c.At - capturas[0].At).TotalSeconds > 60) capturas.RemoveAt(0);
                if (!png) { nCapt++; sumCapt += ms; if (ms > maxCapt) maxCapt = ms; }
                // (el aviso de la imagen tarde puede llegar antes que el de la captura: van por scripts distintos)
                for (int i = tardeSueltas.Count - 1; i >= 0; i--)
                {
                    double d = (tardeSueltas[i] - c.At).TotalMilliseconds;
                    if (d >= -20 && d <= 200) { nCaptTarde++; tardeSueltas.RemoveAt(i); }
                }
            }
        }

        // La captura por fichero ya esta en el disco (ms desde que se pidio a mpv)
        public static void PngListo(double ms) { lock (sync) { nPng++; sumPng += ms; if (ms > maxPng) maxPng = ms; } }

        // (con sync cogido) ¿empezo una captura de la app en los 200 ms antes de esta imagen tarde?
        static bool TrasCaptura(DateTime at)
        {
            for (int i = capturas.Count - 1; i >= 0; i--)
            {
                double d = (at - capturas[i].At).TotalMilliseconds;
                if (d < -20) continue;
                if (d > 200) break;
                return true;
            }
            return false;
        }

        // Una linea "Output cadence" del log de Magpie (y si en esa ventana hubo pico de la grafica)
        public static void MagpieVentana(string line, bool pico)
        {
            try
            {
                Match t = Regex.Match(line, @"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\|");
                Match c = Regex.Match(line, @"captureIntervalP95Ms=([0-9.]+) captureIntervalP99Ms=[0-9.]+ captureIntervalMaxMs=([0-9.]+) outputIntervalMaxMs=([0-9.]+)");
                if (!t.Success || !c.Success) return;
                DateTime at = DateTime.ParseExact(t.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                Ventana v = new Ventana();
                v.Hasta = at;
                v.Desde = ultimaVentana != DateTime.MinValue && (at - ultimaVentana).TotalSeconds < 10 ? ultimaVentana : at.AddSeconds(-4);
                ultimaVentana = at;
                v.CapP95 = Num(c.Groups[1].Value); v.CapMax = Num(c.Groups[2].Value); v.OutMax = Num(c.Groups[3].Value); v.Pico = pico;
                lock (sync) { v.Arranque = ventanasSesion++ == 0; ventanas.Add(v); if (ventanas.Count > 64) ventanas.RemoveAt(0); }
            }
            catch { }
        }

        // (1.2.3 RITMO2) Algo que se pone encima de la imagen o le quita el primer plano (menu de ajustes, otra ventana): los tirones de
        // ese momento son de eso (Windows compone), no del camino de la imagen
        public static void Ventanal(string que)
        {
            Evento e = new Evento(); e.At = DateTime.Now; e.Que = que ?? "";
            lock (sync) { eventos.Add(e); while (eventos.Count > 0 && (e.At - eventos[0].At).TotalSeconds > 60) eventos.RemoveAt(0); }
        }

        // (con sync cogido) lo que hubo encima de la imagen entre a y b (o null)
        static string EventoEntre(DateTime a, DateTime b)
        {
            for (int i = eventos.Count - 1; i >= 0; i--)
                if (eventos[i].At >= a && eventos[i].At <= b) return eventos[i].Que;
            return null;
        }

        // Una linea "DLSSFG frontend timing" del log de Magpie: lo que esperan sus imagenes en su cola (fifoAge) y al reloj (paceWait)
        public static void MagpieEspera(string line)
        {
            Match f = Regex.Match(line, @"paceWait=([0-9.]+) ms.*?fifoAge=([0-9.]+) ms");
            if (!f.Success) return;
            double p = Num(f.Groups[1].Value), a = Num(f.Groups[2].Value);
            if (double.IsNaN(p) || double.IsNaN(a)) return;
            lock (sync) { espera.Add(p); fifo.Add(a); if (fifo.Count > 400) { fifo.RemoveAt(0); espera.RemoveAt(0); } }
        }

        // (1.2.3 RITMO2) "switch2-rate" de ce_power.lua (cada 0,5 s): si la ultima imagen que dejo pasar el filtro de repetidas era nueva
        // ("new") o una copia de una imagen quieta ("copy")
        public static void MpvRitmo(string kind)
        {
            if (kind != "new" && kind != "copy") return;
            RitmoMpv r = new RitmoMpv(); r.At = DateTime.Now; r.Copia = kind == "copy";
            lock (sync) { ritmosMpv.Add(r); while (ritmosMpv.Count > 0 && (r.At - ritmosMpv[0].At).TotalSeconds > 60) ritmosMpv.RemoveAt(0); }
        }

        // (con sync cogido) ¿la imagen estuvo quieta entre a y b? (la mitad o mas de las muestras de mpv eran copias)
        static bool QuietaEntre(DateTime a, DateTime b)
        {
            int n = 0, c = 0;
            foreach (RitmoMpv r in ritmosMpv) if (r.At >= a && r.At <= b) { n++; if (r.Copia) c++; }
            return n >= 2 && 2 * c >= n;
        }

        // (1.2.3 RITMO2) Una linea "DLSSFG presentation ring" del log de Magpie (cada ~1 s): las capturas que entraron y lo que espero su
        // cola a que la pantalla soltara sitio (excludedRingWait). Con espera, las imagenes que llegan mientras tanto se pierden
        public static void MagpieAnillo(string line)
        {
            Match t = Regex.Match(line, @"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\|");
            Match m = Regex.Match(line, @"presentation ring: captured=([0-9.]+) FPS.*?excludedRingWait=([0-9.]+) ms");
            if (!t.Success || !m.Success) return;
            Anillo a = new Anillo();
            a.At = DateTime.ParseExact(t.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            a.Captadas = Num(m.Groups[1].Value); a.Espera = Num(m.Groups[2].Value);
            if (double.IsNaN(a.Espera)) return;
            lock (sync) { anillos.Add(a); while (anillos.Count > 0 && (a.At - anillos[0].At).TotalSeconds > 60) anillos.RemoveAt(0); }
        }

        // (con sync cogido) la mayor espera de la cola de Magpie entre a y b
        static double EsperaColaEntre(DateTime a, DateTime b)
        {
            double mx = 0;
            foreach (Anillo x in anillos) if (x.At >= a && x.At <= b && x.Espera > mx) mx = x.Espera;
            return mx;
        }

        // El origen del peor tiron de las ventanas nuevas (normal = ms entre dos imagenes reales; salida = ms entre dos presentaciones
        // de Magpie, la mitad con la generacion x2). "" si no hubo tiron. Se cuentan todos para el resumen
        public static string Repasa(double normal, double salida, out double peorMs)
        {
            peorMs = 0;
            List<Ventana> vs; List<HuecoMpv> hs;
            lock (sync) { vs = new List<Ventana>(ventanas); ventanas.Clear(); hs = new List<HuecoMpv>(mpv); }
            string texto = ""; double peorDe = 0;
            foreach (Ventana v in vs)
            {
                bool tiron = v.CapMax >= normal * 1.45 && v.CapMax - normal >= 20 && v.CapMax < 400;   // (como antes: lo de 400 ms o mas es un cambio, no un tiron)
                bool salidaMal = !tiron && v.OutMax >= salida * 1.9 && v.OutMax - salida >= 14 && v.OutMax < 400 && v.CapMax < normal * 1.45;
                if (!tiron && !salidaMal) continue;
                int o; string porque, encima;
                lock (sync) encima = EventoEntre(v.Desde.AddMilliseconds(-800), v.Hasta.AddMilliseconds(200));
                bool quieta;
                lock (sync) quieta = tiron && v.CapMax < 1.45 * 50 + 20 && QuietaEntre(v.Desde.AddMilliseconds(-500), v.Hasta);
                if (v.Arranque) { o = 6; porque = "Magpie empezando (sus primeros 4 s: la captura se acaba de abrir y la grafica se pone a ritmo)"; }
                else if (quieta) { o = 9; porque = "la imagen estaba quieta (mpv deja pasar una copia cada 50 ms; no se nota)"; }
                else if (encima != null) { o = 7; porque = encima + " en ese momento (Windows compone la imagen en vez de mostrar a Magpie directamente)"; }
                else if (salidaMal) { o = 5; porque = "Magpie presento con un hueco de " + R(v.OutMax) + " ms con las imagenes llegando bien (su ritmo, la generacion o la cola de la pantalla)"; }
                else
                {
                    double marca = 0, hora = 0; bool captura = false;
                    lock (sync)
                        foreach (HuecoMpv h in hs)
                        {
                            if (h.At < v.Desde.AddMilliseconds(-300) || h.At > v.Hasta.AddMilliseconds(150)) continue;
                            if (h.Marca > marca) marca = h.Marca;
                            if (h.Hora > hora) { hora = h.Hora; captura = TrasCaptura(h.At); }
                        }
                    double cerca = v.CapMax - 0.6 * normal;
                    if (marca >= cerca) { o = 0; porque = "la imagen ya llego tarde a mpv (marca de la capturadora " + R(marca) + " ms: la consola o la capturadora)"; }
                    else if (hora >= cerca && captura) { o = 1; porque = "mpv la saco tarde (" + R(hora) + " ms) justo despues de una captura de la app (la que mira el menu de la consola)"; }
                    else if (hora >= cerca) { o = 2; porque = "mpv la saco tarde (" + R(hora) + " ms; la marca de la capturadora " + (marca > 0 ? R(marca) + " ms" : "normal") + ")"; }
                    else if (v.Pico) { o = 4; porque = "mpv la saco a tiempo" + (hora > 0 ? " (peor " + R(hora) + " ms)" : "") + "; con un pico de la grafica en Magpie"; }
                    else
                    {
                        double cola; lock (sync) cola = EsperaColaEntre(v.Desde.AddMilliseconds(-500), v.Hasta.AddMilliseconds(1100));
                        if (cola >= 3) { o = 8; porque = "mpv la saco a tiempo" + (hora > 0 ? " (peor " + R(hora) + " ms)" : "") + "; Magpie la tiro: su cola hacia la pantalla estaba llena (espero hasta " + R(cola) + " ms por segundo a que soltara sitio)"; }
                        else { o = 3; porque = "mpv la saco a tiempo" + (hora > 0 ? " (peor " + R(hora) + " ms)" : "") + "; se perdio o llego tarde en la captura de Windows (WGC) hasta Magpie"; }
                    }
                }
                double ms = o == 5 || (o >= 6 && !tiron) ? v.OutMax : v.CapMax;
                lock (sync) { cuenta[o]++; if (ms > peor[o]) peor[o] = ms; }
                // (el arranque de Magpie y la imagen quieta van solo al resumen: no son tirones jugando)
                if (o != 6 && o != 9 && ms > peorDe) { peorDe = ms; texto = Nombres[o] + ": " + porque; }
            }
            peorMs = peorDe;
            return texto;
        }

        static double Mediana(List<double> l)
        {
            if (l.Count == 0) return double.NaN;
            List<double> c = new List<double>(l); c.Sort(); return c[c.Count / 2];
        }

        // El resumen (y a cero); null si no hubo nada
        public static string Resumen()
        {
            lock (sync)
            {
                List<string> p = new List<string>();
                for (int i = 0; i < cuenta.Length; i++)
                    if (cuenta[i] > 0) p.Add(Nombres[i] + " " + cuenta[i] + " (peor " + R(peor[i]) + " ms)");
                if (nPng > 0) p.Add("capturas por fichero " + nPng + " (listas en " + R(sumPng / nPng) + " ms de media, peor " + R(maxPng) + ")");
                if (nCapt > 0) p.Add("capturas de la app " + nCapt + " (paran a mpv " + R(sumCapt / nCapt) + " ms de media, peor " + R(maxCapt) + "; " + nCaptTarde + " imagenes de mpv tarde justo despues)");
                if (fifo.Count > 0) p.Add("espera en la cola de Magpie " + Math.Round(Mediana(fifo), 1).ToString(CultureInfo.InvariantCulture) + " ms (al reloj " + Math.Round(Mediana(espera), 1).ToString(CultureInfo.InvariantCulture) + ")");
                Array.Clear(cuenta, 0, cuenta.Length); Array.Clear(peor, 0, peor.Length);
                nCapt = 0; nCaptTarde = 0; sumCapt = 0; maxCapt = 0; nPng = 0; sumPng = 0; maxPng = 0; fifo.Clear(); espera.Clear();
                return p.Count == 0 ? null : string.Join(", ", p.ToArray());
            }
        }

        public static void Limpia() { lock (sync) { ventanas.Clear(); mpv.Clear(); capturas.Clear(); anillos.Clear(); ritmosMpv.Clear(); ultimaVentana = DateTime.MinValue; ventanasSesion = 0; } }

        static string R(double v) { return Math.Round(v).ToString(CultureInfo.InvariantCulture); }
    }
}
