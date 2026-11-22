// CapturaSegura.cs - 21-11-2026 (1.2.1, el usuario: "se me ha quedado congelada la app al salir al menu desde rocket league").
// La red de seguridad del formato de captura: si justo despues de que la app cambie el formato de la capturadora (otra captura, otro modo,
// el menu de la consola, un perfil, el menu de ajustes) la imagen nueva no llega y la consola estaba encendida un momento antes (su imagen
// o su sonido), no se espera para siempre "a que se encienda": se vuelve a un formato que la capturadora si da y se avisa en pantalla.
//
// Log del usuario (sesion de las 19:09:21): Rocket League a 1440p en el modo 120 -> menu de la PS5 a 4K nativo. La app pidio 3840x2160 a
// 120 fps (la GC573 a 4K solo da 24-60: data\hardware.txt), mpv no pudo cambiar en caliente, se reabrio con lo mismo y se quedo sin una
// sola imagen ("Sin senal de la consola: esperando a que se encienda") hasta el Alt+F4, con la consola sonando. La causa ya esta
// arreglada (Config.FramerateNow); esto es para que ningun otro formato que la capturadora no de pueda volver a congelar la app.
//
// Como va:
//  - Cada lanzamiento de la imagen (mpv o el motor, RunMpv) con un formato distinto del ultimo que dio imagen y con la consola encendida
//    hace poco (imagen de la consola o sonido en los ultimos 10 s) se vigila: sin ninguna imagen en captura_segura_s (6 s mpv; 12 s el
//    motor recien abierto, que antes arranca Streamline) se cierra y se prueba el siguiente formato. Tambien si se cierra solo sin imagen
//    (mpv sale con 2 si la capturadora no acepta el formato).
//  - Orden: el mismo tamaño a la velocidad de siempre (framerate=, 59,94; lo que da cualquier capturadora en cualquier tamaño de la app),
//    luego el ultimo formato que dio imagen, luego 1080p a la velocidad de siempre y, por ultimo, 1080p con el formato de pixel que elija
//    la capturadora. (La primera idea era empezar por el ultimo que funciono; pero al volver al menu de la consola el ultimo es el del
//    juego -1440p a 120-, que ni es el tamaño del menu ni quita los 120 que fallaban: primero se cambia solo lo menos posible.)
//  - El cambio solo se queda (y se avisa) cuando el formato de repuesto DA imagen; si ninguno la da (la consola se apago justo a la vez),
//    no se guarda nada y se espera como siempre con lo pedido. Lo guardado vale 3 minutos: despues lo pedido se vuelve a probar (la
//    capturadora puede dar 120 cuando la consola ya va a 120 Hz) y, si sigue sin ir, otra vez el repuesto.
//  - Con el sonido de la consola en silencio (un cambio de modo de video de la consola tambien lo calla) se espera mas (15 s) y solo si
//    antes se veia la consola: asi un cambio de modo de la PS5 al arrancar un juego no se toma por un formato malo.
//  - Nunca es la consola apagada: un mpv / motor recien abierto no tiene la vigilancia de apagado armada (no ha visto la consola).
// Solo pruebas (con test_source): test_captura_mala=WxH@fps[,...] hace que esos formatos no den imagen (test_captura_mala_url: lo que se
// abre entonces; de serie una fuente que no se puede abrir, mpv sale con 2; con un tcp:// a un puerto que acepta y no manda, se queda
// colgado sin imagen, como la GC573 a 4K a 120).
// Lo compila build.bat con el resto (partes de Config y TrayApp de Switch2Magpie.cs). C# 5
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace ConsoleEnhancer
{
    // Un formato de captura: tamaño (WxH), velocidad (como va en framerate=: "60000/1001", "120") y formato de pixel de mpv ("" = el de la
    // capturadora)
    sealed class CapFmt
    {
        public readonly string Size, Fps, Pix;
        public CapFmt(string size, string fps, string pix) { Size = size ?? ""; Fps = fps ?? ""; Pix = pix ?? ""; }
        public string Key { get { return Size + "@" + Fps + "@" + Pix; } }
        public bool Same(CapFmt o) { return o != null && o.Key == Key; }
        public string SizeFps { get { return Size + "@" + Fps; } }
        public string Desc()
        {
            string n = Size == "1280x720" ? "720p" : Size == "1920x1080" ? "1080p" : Size == "2560x1440" ? "1440p" : Size == "3840x2160" ? "4K" : Size;
            return n + Deteccion.Tx.T(" a ", " at ") + FpsText(Fps) + " fps";
        }
        public static string FpsText(string fps)
        {
            string[] p = (fps ?? "").Split('/');
            double n, d = 1;
            if (p.Length == 0 || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return fps;
            if (p.Length > 1 && (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d == 0)) d = 1;
            return Deteccion.Tx.N(Math.Round(n / d, 2), "0.##");
        }
    }

    static class CapturaSegura
    {
        static readonly object sync = new object();
        // Lo pedido que no dio imagen -> el repuesto que si (con su hora: vale ReplaceSecs)
        static readonly Dictionary<string, KeyValuePair<CapFmt, DateTime>> replace = new Dictionary<string, KeyValuePair<CapFmt, DateTime>>();
        const double ReplaceSecs = 180;
        // El que se esta probando ahora en lugar de 'trialFor' (aun sin imagen)
        static CapFmt trial, trialFor;

        // Lo que la app pide ahora (sin repuestos)
        public static CapFmt Pedido(Config cfg) { return new CapFmt(cfg.CaptureNow, cfg.FramerateNow, cfg.PixelFormatNow); }

        // Lo que se abre de verdad: lo pedido, o su repuesto (el que se esta probando o uno que ya funciono hace menos de 3 min)
        public static CapFmt Efectivo(Config cfg)
        {
            CapFmt p = Pedido(cfg);
            if (!cfg.CapturaSegura) return p;
            lock (sync)
            {
                if (trial != null && p.Same(trialFor)) return trial;
                KeyValuePair<CapFmt, DateTime> r;
                if (replace.TryGetValue(p.Key, out r))
                {
                    // (otro tamaño: la capturadora no tiene ese tamaño, no cambia en la sesion; la velocidad si puede, con la consola a 120 Hz)
                    if (r.Key.Size != p.Size || (DateTime.Now - r.Value).TotalSeconds < ReplaceSecs) return r.Key;
                    replace.Remove(p.Key);
                    Log.Write("Captura segura: " + p.Desc() + " se vuelve a probar (el repuesto " + r.Key.Desc() + " ya tiene " + (int)ReplaceSecs + " s)");
                }
            }
            return p;
        }

        public static void Probar(CapFmt pedido, CapFmt repuesto) { lock (sync) { trialFor = pedido; trial = repuesto; } }
        public static void SinPrueba() { lock (sync) { trial = null; trialFor = null; } }
        public static CapFmt Prueba { get { lock (sync) return trial; } }

        // El repuesto da imagen: se queda para lo pedido
        public static void Guardar(CapFmt pedido, CapFmt repuesto)
        {
            lock (sync)
            {
                replace[pedido.Key] = new KeyValuePair<CapFmt, DateTime>(repuesto, DateTime.Now);
                trial = null; trialFor = null;
            }
        }

        // Los formatos de repuesto para 'fallido' (lo pedido: 'pedido'), en orden, sin los ya probados en este episodio
        public static List<CapFmt> Candidatos(Config cfg, CapFmt pedido, CapFmt bueno, HashSet<string> probados)
        {
            List<CapFmt> l = new List<CapFmt>();
            string fps = cfg.Framerate;   // (la de siempre: 60000/1001)
            Action<CapFmt> add = delegate (CapFmt f)
            {
                if (f == null || f.Size.Length == 0 || probados.Contains(f.Key) || f.Same(pedido)) return;
                foreach (CapFmt x in l) if (x.Same(f)) return;
                l.Add(f);
            };
            if (pedido.Fps != fps) add(new CapFmt(pedido.Size, fps, pedido.Pix));
            if (bueno != null) add(bueno);
            add(new CapFmt("1920x1080", fps, cfg.PixelFormat));
            add(new CapFmt("1920x1080", fps, ""));
            // 22-11 (1.2.2, UNIVERSAL): capturadoras que solo listan 60,000 exactos (muchas UVC de USB): ffmpeg (dshow) no abre 59,94 fuera
            // de su rango; y al reves (la de siempre ya es 60)
            string otra = fps == "60000/1001" ? "60" : fps == "60" ? "60000/1001" : null;
            if (otra != null) { add(new CapFmt(pedido.Size, otra, pedido.Pix)); add(new CapFmt("1920x1080", otra, "")); }
            return l;
        }

        // ---- Solo pruebas (test_source): formatos que la "capturadora" no da ----
        public static bool TestMala(Config cfg, CapFmt f)
        {
            if (cfg.TestSource.Length == 0 || cfg.TestCapturaMala.Length == 0 || f == null) return false;
            foreach (string m in cfg.TestCapturaMala) if (m == f.SizeFps || m == f.Size) return true;
            return false;
        }
    }

    partial class Config
    {
        // ini captura_segura (21-11, 1.2.1, src\Lanzador\CapturaSegura.cs): 1 = sin imagen tras cambiar de formato con la consola encendida,
        // se vuelve a uno que la capturadora da; 0 = como antes (esperar). captura_segura_s: los segundos sin imagen con mpv (el motor, el doble)
        public bool CapturaSegura = true;
        public double CapturaSeguraSecs = 6;
        public string[] TestCapturaMala = new string[0];          // (solo pruebas: test_captura_mala=WxH@fps,...)
        public string TestCapturaMalaUrl = "av://lavfi:testsrc=size=0x0";   // (solo pruebas: test_captura_mala_url)
        public bool TestSonido = false;                           // (solo pruebas con test_source: test_sonido=1, la consola "suena" siempre)

        public bool ParseCapturaSegura(string key, string val)
        {
            switch (key)
            {
                case "captura_segura": CapturaSegura = val.Trim() != "0"; return true;
                case "captura_segura_s":
                    {
                        double s;
                        if (double.TryParse(val.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out s)) CapturaSeguraSecs = Math.Max(2, Math.Min(30, s));
                        return true;
                    }
                case "test_captura_mala":
                    TestCapturaMala = val.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    return true;
                case "test_captura_mala_url": TestCapturaMalaUrl = val.Trim(); return true;
                case "test_sonido": TestSonido = val.Trim() == "1"; return true;
            }
            return false;
        }
    }

    partial class TrayApp
    {
        // ---- El ultimo formato que dio imagen y cuando se vio la consola por ultima vez ----
        volatile CapFmt capBueno;                      // el ultimo que dio imagen (mpv o el motor)
        DateTime capConsolaVista = DateTime.MinValue;  // la ultima imagen de la consola (no la pantalla No Signal)
        DateTime capSonido = DateTime.MinValue;        // el ultimo "sound" del medidor de audio
        volatile string capAudio = "";                 // el ultimo estado del medidor ("sound" / "silent" / "")
        volatile bool capEnSitioFallo;                 // el ultimo cambio sin cerrar mpv fallo: la reapertura se vigila con menos tiempo
        volatile string capAviso;                      // el aviso para cuando se vea la imagen (Osd)

        // ---- El episodio en curso: lo pedido, el lanzamiento vigilado y lo ya probado ----
        CapFmt capPedido;                               // lo que se pidio de verdad (sin repuesto) en este episodio
        volatile CapFmt capVigilado;                    // el formato de este lanzamiento si se vigila (null = no)
        int capVigilaMs;                                // sin imagen tanto (desde imageStart) = no lo da
        bool capLanzEng, capLanzWoke;                   // este lanzamiento: el motor / un motor en espera despertado (su fallo no es del formato)
        readonly HashSet<string> capProbados = new HashSet<string>();
        volatile bool capRelanzarYa;                    // RunMpv: relanzar al momento con el repuesto (sin contarlo como fallo)
        string capRendidoKey;                           // lo pedido con lo que ya no quedaba nada que probar (60 s sin otro episodio)
        DateTime capRendidoAt = DateTime.MinValue;

        // MpvLinkLoop, cada 0,1 s: la consola se ve (imagen que avanza y no es la pantalla No Signal)
        void CapNotaConsola(DateTime now)
        {
            if (consoleOn && signalOn && videoSignal)
            {
                double adv;
                lock (linkSync) adv = (now - lastAdvance).TotalMilliseconds;
                if (adv < 600) capConsolaVista = now;
            }
        }

        // MpvLinkLoop, cada 0,5 s: lo que dice el medidor de audio de la consola
        void CapNotaAudio(string state)
        {
            capAudio = state ?? "";
            if (state == "sound") capSonido = DateTime.Now;
        }

        bool CapConsolaReciente(double secs)
        {
            DateTime now = DateTime.Now;
            CapSonidoPrueba(now);
            return (now - capConsolaVista).TotalSeconds < secs || (now - capSonido).TotalSeconds < secs;
        }

        // (solo pruebas: con test_source y test_sonido=1 la consola "suena" siempre, como el medidor de audio con la consola encendida)
        void CapSonidoPrueba(DateTime now)
        {
            if (cfg.TestSonido && cfg.TestSource.Length > 0) { capSonido = now; capAudio = "sound"; }
        }

        // RunMpv, justo antes de lanzar (o despertar) la imagen: ¿se vigila este lanzamiento?
        void CapLanzamiento(bool eng, bool woke)
        {
            capRelanzarYa = false;
            capVigilado = null;
            mpvSinNada = null;
            capLanzEng = eng;
            capLanzWoke = woke;
            if (!cfg.CapturaSegura) return;
            try
            {
                CapFmt pedido = CapturaSegura.Pedido(cfg);
                CapFmt ef = CapturaSegura.Efectivo(cfg);
                // Lo pedido ha cambiado (otro cambio a medias): episodio nuevo
                if (capPedido != null && !capPedido.Same(pedido))
                {
                    if (CapturaSegura.Prueba != null) CapturaSegura.SinPrueba();
                    capPedido = null;
                    capProbados.Clear();
                }
                if (capPedido == null)
                {
                    // Igual que el ultimo que dio imagen: nada que vigilar. Sin la consola encendida hace poco, tampoco (se espera como siempre);
                    // ni con lo pedido con lo que se acaba de probar todo (60 s)
                    if (ef.Same(capBueno)) return;
                    if (!CapConsolaReciente(10)) return;
                    if (pedido.Key == capRendidoKey && (DateTime.Now - capRendidoAt).TotalSeconds < 60) return;
                    capPedido = pedido;
                    capProbados.Clear();
                }
                capProbados.Add(ef.Key);
                capVigilado = ef;
                double s = cfg.CapturaSeguraSecs;
                // (mpv da su primera imagen en ~1,1-1,5 s; el motor recien abierto en 4,5-7,5 s, antes Streamline; el despertado en ~0,4 s)
                capVigilaMs = (int)(1000 * (eng ? (woke ? Math.Max(4, s * 0.7) : s * 3) : (capEnSitioFallo ? Math.Max(3, s * 0.7) : s)));
                capEnSitioFallo = false;
            }
            catch (Exception ex) { Log.Error("Captura segura (al lanzar)", ex); capVigilado = null; }
        }

        // 22-11 (1.2.2, ERRORES): el enlace (la tuberia) del mpv / motor de ahora, y cuando se hizo (MpvLinkLoop al conectar). El motor recien
        // abierto la crea al acabar de arrancar, justo antes de abrir la capturadora
        volatile Process capEnlaceDe;
        DateTime capEnlaceAt = DateTime.MinValue;
        void CapEnlace(Process p) { capEnlaceAt = DateTime.Now; capEnlaceDe = p; }
        // ¿El motor p, recien abierto, sigue arrancando (Streamline, shaders) sin haber abierto aun la capturadora?
        bool CapMotorArrancando(Process p) { return Config.EngineActive && capLanzEng && !capLanzWoke && capEnlaceDe != p; }

        // WaitForSignal, mientras no hay imagen: true = este formato no da imagen (se cierra y se relanza con el repuesto ya puesto)
        bool CapSinImagen(Process p)
        {
            if (!cfg.CapturaSegura || capLanzWoke) return false;
            CapSonidoPrueba(DateTime.Now);
            double ms = (DateTime.Now - imageStart).TotalMilliseconds;
            // 22-11 (1.2.2, ERRORES): el motor recien abierto no abre la capturadora hasta acabar de arrancar (Streamline ~4 s y, la primera vez
            // en un PC o en una version nueva sin la cache, compilar sus shaders: 22-11 01:50:06, 4K sin imagen a los 20 s compilando aun ->
            // "la capturadora no da 4K", 1080p el resto de la sesion y el aviso en pantalla, con la PS5 dando 4K). Hasta su enlace no cuenta;
            // desde el (abre la capturadora a los ~0,2 s), como mpv. Uno que no llega a arrancar lo cierra WaitForSignal a los 30 s
            bool desdeEnlace = capLanzEng;
            if (desdeEnlace)
            {
                if (capEnlaceDe != p) return false;
                ms = (DateTime.Now - capEnlaceAt).TotalMilliseconds;
            }
            bool sonando = (DateTime.Now - capSonido).TotalSeconds < 3;
            // mpv sin nada abierto (no pudo abrir la capturadora con este formato; mpv.exe se queda esperando en vez de salir): ya, sin esperar
            bool sinNada = mpvSinNada == p && !capLanzEng && ms > 300;
            CapFmt v = capVigilado;
            if (v != null && sinNada) return CapSiguiente(v, "no se ha podido abrir (mpv sin nada abierto a los " + (int)ms + " ms)");
            if (v == null)
            {
                // Sin vigilar (al abrir la app, o sin ver antes la consola) pero la consola suena y no llega ni una imagen en el doble de tiempo:
                // tambien es el formato (p. ej. uno que la capturadora no da guardado en el perfil del juego de la ultima vez)
                if (capPedido != null || !sonando) return false;
                if (!sinNada && ms < 1000 * cfg.CapturaSeguraSecs * 2) return false;   // (el motor tambien: ms ya va desde su enlace)
                CapFmt pedido = CapturaSegura.Pedido(cfg);
                if (pedido.Key == capRendidoKey && (DateTime.Now - capRendidoAt).TotalSeconds < 60) return false;
                v = CapturaSegura.Efectivo(cfg);
                capPedido = pedido;
                capProbados.Clear();
                capProbados.Add(v.Key);
                return CapSiguiente(v, "sin ninguna imagen en " + (int)ms + " ms" + (desdeEnlace ? " desde que arranco el motor" : "") + " con la consola sonando");
            }
            int vigilaMs = desdeEnlace ? (int)(1000 * cfg.CapturaSeguraSecs) : capVigilaMs;   // (desde el enlace del motor: lo de mpv)
            if (ms < vigilaMs) return false;
            // Con la consola callada (un cambio de modo de video tambien la calla), mas tiempo y solo si antes se veia su imagen
            if (!sonando && capAudio == "silent")
            {
                if (ms < Math.Max(vigilaMs, 15000)) return false;
                if ((DateTime.Now - capConsolaVista).TotalSeconds > 10 + ms / 1000.0) { CapRendirse(v, "la consola en silencio y sin imagen: puede estar apagada"); return false; }
            }
            return CapSiguiente(v, "sin ninguna imagen en " + (int)ms + " ms" + (desdeEnlace ? " desde que arranco el motor" : "") + (sonando ? " con la consola sonando" : ""));
        }

        // mpv que se ha quedado sin nada abierto (idle-active, MpvLinkLoop / MpvReader; null = ninguno)
        volatile Process mpvSinNada;
        DateTime mpvSinNadaLog = DateTime.MinValue;

        // WaitForSignal, sin formato nuevo que vigilar: mpv sin nada abierto no va a dar imagen nunca (la capturadora no se pudo abrir: ocupada
        // por otro programa, desenchufada, un formato que no da sin la consola vista antes...). true = se cierra y RunMpv lo reintenta (con su
        // tope de intentos y su aviso, mpvFails); antes se quedaba "esperando a que se encienda" para siempre
        bool MpvSinNada(Process p)
        {
            if (mpvSinNada != p || Config.EngineActive || userExit) return false;
            if ((DateTime.Now - imageStart).TotalMilliseconds < 1500) return false;   // (un momento, por si acaba de abrirse)
            mpvSinNada = null;
            if ((DateTime.Now - mpvSinNadaLog).TotalSeconds > 30)
            {
                mpvSinNadaLog = DateTime.Now;
                Log.Write("mpv no ha podido abrir la capturadora (" + CapturaSegura.Efectivo(cfg).Desc() + "): se queda sin nada abierto; se cierra y se vuelve a intentar");
            }
            return true;
        }

        // RunMpv: la imagen se ha cerrado sin dar imagen. true = relanzar ya (con el repuesto o porque se cerro por esto)
        bool CapFallo(bool eng, bool woke, int salida)
        {
            if (capRelanzarYa) { capRelanzarYa = false; return true; }
            CapFmt v = capVigilado;
            if (v == null || (eng && woke) || salida == 0 || userExit) return false;
            if (!CapSiguiente(v, "se ha cerrado sin imagen (codigo " + salida + ")")) return false;
            capRelanzarYa = false;
            return true;
        }

        // El siguiente formato que probar. true = se cierra / relanza ya (con el repuesto, o con lo pedido si ya no queda nada: asi lo que
        // se abre es lo que la app cree que esta abierto)
        bool CapSiguiente(CapFmt fallido, string por)
        {
            CapFmt pedido = capPedido ?? fallido;
            List<CapFmt> c = CapturaSegura.Candidatos(cfg, pedido, capBueno, capProbados);
            capVigilado = null;
            if (c.Count == 0)
            {
                bool habiaPrueba = CapturaSegura.Prueba != null;
                CapRendirse(pedido, fallido.Desc() + " " + por + " y no queda otro formato que probar");
                if (!habiaPrueba) return false;
                capRelanzarYa = true;
                return true;
            }
            CapFmt next = c[0];
            CapturaSegura.Probar(pedido, next);
            capRelanzarYa = true;
            Log.Write("Captura segura: " + fallido.Desc() + (fallido.Pix.Length > 0 ? " (" + fallido.Pix + ")" : "") + " " + por +
                      ": la capturadora no lo da; se prueba " + next.Desc() + (next.Pix.Length > 0 ? " (" + next.Pix + ")" : "") +
                      " (no es la consola apagada)");
            return true;
        }

        void CapRendirse(CapFmt pedido, string por)
        {
            Log.Write("Captura segura: " + por + ": se sigue con lo pedido y se espera como siempre");
            CapturaSegura.SinPrueba();
            capRendidoKey = pedido != null ? pedido.Key : null;
            capRendidoAt = DateTime.Now;
            capPedido = null;
            capVigilado = null;
            capProbados.Clear();
        }

        // La imagen ha llegado (WaitForSignal): ese formato funciona. Si era un repuesto, se queda y se avisa
        void CapImagen()
        {
            if (!cfg.CapturaSegura) return;
            try
            {
                CapFmt ef = CapturaSegura.Efectivo(cfg);
                CapFmt pedido = capPedido;
                capBueno = ef;
                capVigilado = null;
                if (pedido != null && !pedido.Same(ef))
                {
                    CapturaSegura.Guardar(pedido, ef);
                    Log.Write("Captura segura: " + ef.Desc() + " da imagen: se queda en lugar de " + pedido.Desc() +
                              (ef.Size != pedido.Size ? " (el resto de la sesion: es otro tamaño)" : " (3 min; luego se vuelve a probar)"));
                    capAviso = AvisoRepuesto(pedido, ef);
                }
                else CapAvisoOtraVez();
                capPedido = null;
                capProbados.Clear();
            }
            catch (Exception ex) { Log.Error("Captura segura (imagen)", ex); }
        }

        // Un cambio de captura sin cerrar mpv / el motor ha dado imagen: ese formato funciona
        void CapCambioHecho()
        {
            if (!cfg.CapturaSegura) return;
            try
            {
                capBueno = CapturaSegura.Efectivo(cfg);
                CapAvisoOtraVez();
                CapMostrarAviso();
            }
            catch { }
        }

        static string AvisoRepuesto(CapFmt pedido, CapFmt ef)
        {
            return Deteccion.Tx.T("La capturadora no da " + pedido.Desc() + " · se usa " + ef.Desc(),
                                  "The capture card can't do " + pedido.Desc() + " · using " + ef.Desc());
        }

        // Lo pedido otra vez con un repuesto ya guardado (se abre el repuesto al momento, sin esperar): el aviso tambien, como mucho cada 30 s
        string capAvisoClave;
        DateTime capAvisoAt = DateTime.MinValue;
        void CapAvisoOtraVez()
        {
            CapFmt pedido = CapturaSegura.Pedido(cfg), ef = CapturaSegura.Efectivo(cfg);
            if (pedido.Same(ef)) return;
            string k = pedido.Key + ">" + ef.Key;
            if (k == capAvisoClave && (DateTime.Now - capAvisoAt).TotalSeconds < 30) return;
            capAvisoClave = k;
            capAvisoAt = DateTime.Now;
            capAviso = AvisoRepuesto(pedido, ef);
        }

        // Con la imagen ya a la vista (RunMpv, tras quitar el telon): el aviso del repuesto
        void CapMostrarAviso()
        {
            string a = capAviso;
            if (a == null) return;
            capAviso = null;
            capAvisoClave = CapturaSegura.Pedido(cfg).Key + ">" + CapturaSegura.Efectivo(cfg).Key;
            capAvisoAt = DateTime.Now;
            lock (cmdQueue) cmdQueue.Add(OsdCmd(a, "6000", true));
            Log.Write("Captura segura: aviso en pantalla: " + a);
        }
    }
}
