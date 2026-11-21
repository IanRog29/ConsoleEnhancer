// Consolas.cs - Que consola hay detras de la capturadora (18-11, agente CONSOLAS). La app nacio para la Switch 2; ahora vale para
// cualquier consola. Un perfil por consola con lo que la app necesita saber de ella: que capturas tienen sentido (lo que saca la
// consola por HDMI), si su menu se reconoce (el HOME a 4K nativo y los juegos por su icono: solo el de la Switch 2), los modos de
// sus juegos, los nombres de sus botones para los avisos, el consejo de HDCP y como se ve que se ha apagado (cuanto esperar a una
// pantalla "No Signal" antes de darla por apagada: al arrancar un juego la PS5 o la Xbox pueden cambiar de modo de video).
// Se elige con consola= en switch2.ini: auto (de serie) | switch2 | switch1 | ps5 | ps4 | xboxseries | xboxone | otra. Con auto,
// la ultima vista (games\console.txt; sin ella la Switch 2 si ya se vio su HOME en este PC, games\home_ref.txt, y si no "otra")
// y lo que se vea en la imagen (Pista*/Visto*: el HOME de Nintendo, palabras leidas por el OCR, la resolucion de verdad de un menu,
// la cadencia y, si algun dia se sabe leer, la señal de entrada de la capturadora). Lo que dice cada consola: docs\CONSOLAS.md.
// En los textos de pantalla: "consola" a secas (Visible). C# 5
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleEnhancer
{
    // Lo que la app necesita saber de una consola. Los textos con tildes son de pantalla; los de log, sin
    sealed class ConsolaPerfil
    {
        public string Clave;          // la del ini (consola=)
        public string Nombre;         // "Nintendo Switch 2" (log, menu de la bandeja)
        public string Corto;          // "Switch 2" (log)
        public string Familia;        // "nintendo" | "playstation" | "xbox" | "otra"
        // 20-11 (CONSOLA): probada con la consola de verdad en la capturadora (la Switch 2 y la PS5). Con consola=auto, a una sin probar
        // solo se cambia con pruebas propias suyas (nunca por la hora) y su apagado va con lo mas prudente (Consolas.ApagadoPrudente)
        public bool Probada;

        // ---- Imagen (lo que saca por HDMI hacia la GC573: 4K60, 1440p120, 1080p240 como mucho; sin 4K120 ni VRR) ----
        public int SalidaMaxAlto;     // la salida mas alta (1080: la captura de 1440p o 4K solo seria la capturadora agrandando)
        public bool Da1440;           // ofrece 1440p (si la capturadora la anuncia: la GC573 si)
        public bool Da120;            // puede sacar 120 Hz (a 1080p o 1440p con esta capturadora)
        public bool Hdr;              // puede sacar HDR10 (la GC573 lo anuncia: la consola lo pondra si lo tiene en automatico)
        public bool Hz5994;           // a 60 saca 59,94 (60000/1001) y no 60,00
        public string[] Capturas;     // las capturas (WxH) que tienen sentido, de menor a mayor
        public string CapturaDeSerie; // la de partida (sin nada guardado)

        // ---- Menu de la consola ----
        public bool TieneHome;        // la app reconoce su menu (HomeLabel: nombre cian, franjas del HOME): HOME a 4K nativo y juegos por icono
        public bool HomeNativo4K;     // en el menu, la captura de 4K tal cual (sin escalar): solo si el menu sale a 4K
        public string Menu;           // como se llama su menu en los avisos ("HOME", "pantalla de inicio", "Inicio")
        // 19-11: su menu se reconoce con su propio lector (src\MenuPs5.cs: la hora de la PS5 arriba a la derecha, la del PC): el menu a 4K
        // nativo sin Magpie, sin generacion ni Smooth Motion; los juegos con los ajustes de toda la consola ("Consola")
        // (19-11, DETECCION: tambien la Xbox, por la hora del PC en la franja de arriba, src\DetectorMenu.cs; SIN PROBAR con la consola)
        public bool MenuPropio;

        // ---- Juegos ----
        public bool Juegos120;        // hay juegos a 120 imagenes (la captura va a 60: se ven 60)
        public bool Juegos40;         // hay modos de 40 (con la consola a 120 Hz; a 60 Hz no existen)
        public bool ResolucionDinamica; // la mayoria de juegos cambia de resolucion por dentro (la salida sigue fija)

        // ---- Botones (para los avisos) ----
        public string BotonAceptar, BotonAtras, BotonMenu, BotonCaptura, BotonVista, BotonOpciones;
        public string[] BotonesCara;  // los cuatro de la derecha, en el orden A/B/X/Y de XInput (abajo, derecha, izquierda, arriba)
        public string Gatillos;       // "L/R y ZL/ZR", "L1/R1 y L2/R2", "LB/RB y LT/RT"

        // ---- HDCP ----
        public bool HdcpEnJuegos;     // con HDCP puesto en la consola, la capturadora no da imagen de los juegos (PS4 / PS5)
        public string ConsejoHdcp;    // aviso para quitarlo (null = no hace falta)

        // ---- Apagado / reposo (la capturadora enseña "No Signal" en cuanto deja de llegar la señal HDMI) ----
        public bool ApagadoPorMenu;   // la app ve su menu de apagado y sabe si se esta arrancando un juego (los tiempos del ini tal cual)
        public double EsperaSinSenalMinS; // "No Signal" jugando: como poco esto antes de darla por apagada (cambios de modo de video)
        public double EsperaNegroS;   // negro y silencio seguidos: apagada (el script de mpv, black_secs)
        public string Reposo;         // como llama la consola a su reposo (log): "modo de espera", "modo de reposo"...
        public string[] TextosApagado; // su menu de apagado (OCR, en minusculas)
        public string[] TextosMarca;  // palabras que la delatan en pantalla (OCR, en minusculas)

        // ---- Ajustes de la consola recomendados (para un aviso o el menu de la bandeja) ----
        public string AjustesVideo;   // donde estan sus ajustes de imagen
        public string ConsejoVideo;   // lo recomendado con esta app (la captura va en SDR a 59,94: sin HDR, a 60 Hz)
        public string ConsejoAudio;   // ajuste de sonido recomendado para la capturadora (PCM)

        public static int Alto(string size)
        {
            if (string.IsNullOrEmpty(size)) return 0;
            int x = size.IndexOf('x');
            int h;
            return x > 0 && int.TryParse(size.Substring(x + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out h) ? h : 0;
        }

        // ¿Tiene sentido esta captura con esta consola? (mas alta que lo que saca, la capturadora solo agranda)
        public bool CapturaUtil(string size)
        {
            if (Capturas == null) return true;
            foreach (string c in Capturas) if (c == size) return true;
            return false;
        }

        // La captura que se usa si la pedida no tiene sentido: la mas alta de las suyas que no pase de la pedida
        public string CapturaCercana(string size)
        {
            if (CapturaUtil(size) || Capturas == null || Capturas.Length == 0) return size;
            int want = Alto(size);
            string best = Capturas[0];
            foreach (string c in Capturas) if (Alto(c) <= want) best = c;
            return best;
        }

        // El aviso cuando se pide una captura que no tiene sentido (null si lo tiene)
        public string AvisoCaptura(string size, string sizeName)
        {
            if (CapturaUtil(size)) return null;
            return "Captura " + sizeName + " · la consola da como mucho " + (SalidaMaxAlto >= 2160 ? "4K" : SalidaMaxAlto + "p");
        }

        public string NombreCaptura(int alto) { return alto >= 2160 ? "4K" : alto + "p"; }

        // Para el log: lo que se hace distinto con esta consola
        public string Resumen()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Nombre).Append(": salida hasta ").Append(NombreCaptura(SalidaMaxAlto));
            if (Da120) sb.Append(", 120 Hz");
            if (Hdr) sb.Append(", HDR10");
            sb.Append(", capturas ");
            for (int i = 0; i < Capturas.Length; i++) sb.Append(i > 0 ? "/" : "").Append(NombreCaptura(Alto(Capturas[i])));
            sb.Append(TieneHome ? "; HOME reconocido (" + (HomeNativo4K ? "a 4K nativo, " : "") + "juegos por su icono)" :
                      MenuPropio ? "; menu reconocido por su hora arriba" + (Familia == "xbox" ? " (SIN PROBAR con la consola)" : " a la derecha") + " (a 4K nativo sin Magpie ni Smooth Motion; cada juego con su perfil por su nombre en el centro de control / conmutador, si " + (Familia == "xbox" ? "xbox" : "ps5") + "_reconocer_juegos)" :
                      "; menu sin reconocer (un perfil para toda la consola)");
            sb.Append("; No Signal jugando: ").Append(ApagadoPorMenu ? "lo del ini" : "como poco " + Fmt(EsperaSinSenalMinS) + " s");
            if (HdcpEnJuegos) sb.Append("; HDCP: hay que quitarlo en la consola");
            return sb.ToString();
        }

        static string Fmt(double v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }

        // El perfil de juego de las consolas cuyo menu no se reconoce (games\profiles.txt): lo cambiado con el mando se recuerda ahi y
        // no se mezcla con los juegos de la Switch 2. Uno solo, "Consola" (sale en algun aviso: el usuario quiere "consola" a secas, sin
        // el nombre de la detectada); la captura que no tenga sentido con la consola de ahora la corrige CapturaCercana
        public const string NombrePerfilConsola = "Consola";
        public string PerfilConsola { get { return NombrePerfilConsola; } }
    }

    static class Consolas
    {
        public const string Auto = "auto";
        // En los textos de pantalla: "consola" a secas (el usuario: "que ponga consola y ya")
        public const string Visible = "consola";
        public const string TextoEnciende = "Enciende la consola";
        public const string TextoEnciendeDetalle = "La imagen aparecerá en cuanto se encienda";

        static readonly string[] Todas4 = { "1280x720", "1920x1080", "2560x1440", "3840x2160" };
        static readonly string[] Hasta1080 = { "1280x720", "1920x1080" };

        public static readonly ConsolaPerfil Switch2 = new ConsolaPerfil
        {
            Clave = "switch2", Nombre = "Nintendo Switch 2", Corto = "Switch 2", Familia = "nintendo", Probada = true,
            SalidaMaxAlto = 2160, Da1440 = true, Da120 = true, Hdr = true, Hz5994 = true,
            Capturas = Todas4, CapturaDeSerie = "2560x1440",
            TieneHome = true, HomeNativo4K = true, Menu = "HOME",
            Juegos120 = true, Juegos40 = false, ResolucionDinamica = true,
            BotonAceptar = "A", BotonAtras = "B", BotonMenu = "HOME", BotonCaptura = "Captura", BotonVista = "−", BotonOpciones = "+",
            BotonesCara = new string[] { "B", "A", "Y", "X" }, Gatillos = "L/R y ZL/ZR",
            HdcpEnJuegos = false, ConsejoHdcp = null,
            ApagadoPorMenu = true, EsperaSinSenalMinS = 0, EsperaNegroS = 30, Reposo = "modo de espera",
            TextosApagado = new string[] { "apagar", "reiniciar", "modo de espera", "modo de suspens", "opciones de energ", "power off", "restart", "sleep mode", "power options" },
            TextosMarca = new string[] { "nintendo switch 2", "switch 2", "gamechat", "game chat", "tarjetas de juego virtuales", "virtual game cards" },
            AjustesVideo = "Configuración de la consola > Pantalla", ConsejoVideo = "Resolución 2160p (el HOME a 4K nativo), salida a 120 Hz desactivada, HDR desactivado, rango RGB automático",
        };

        public static readonly ConsolaPerfil Switch1 = new ConsolaPerfil
        {
            Clave = "switch1", Nombre = "Nintendo Switch", Corto = "Switch", Familia = "nintendo",
            SalidaMaxAlto = 1080, Da1440 = false, Da120 = false, Hdr = false, Hz5994 = true,
            Capturas = Hasta1080, CapturaDeSerie = "1920x1080",
            // (su HOME se parece al de la Switch 2, pero el lector se ajusto con capturas de la Switch 2: sin probar, apagado)
            TieneHome = false, HomeNativo4K = false, Menu = "HOME",
            Juegos120 = false, Juegos40 = false, ResolucionDinamica = true,
            BotonAceptar = "A", BotonAtras = "B", BotonMenu = "HOME", BotonCaptura = "Captura", BotonVista = "−", BotonOpciones = "+",
            BotonesCara = new string[] { "B", "A", "Y", "X" }, Gatillos = "L/R y ZL/ZR",
            HdcpEnJuegos = false, ConsejoHdcp = null,
            // (no cambia de modo de video al arrancar un juego: siempre la misma salida)
            ApagadoPorMenu = false, EsperaSinSenalMinS = 3, EsperaNegroS = 30, Reposo = "modo de suspensión",
            TextosApagado = new string[] { "apagar", "reiniciar", "modo de suspens", "opciones de energ", "power off", "restart", "sleep mode", "power options" },
            TextosMarca = new string[] { "nintendo switch online", "nintendo eshop", "nintendo switch" },
            AjustesVideo = "Configuración de la consola > Ajustes de TV", ConsejoVideo = "Resolución 1080p, rango RGB automático",
        };

        public static readonly ConsolaPerfil Ps5 = new ConsolaPerfil
        {
            Clave = "ps5", Nombre = "PlayStation 5", Corto = "PS5", Familia = "playstation", Probada = true,
            SalidaMaxAlto = 2160, Da1440 = true, Da120 = true, Hdr = true, Hz5994 = true,
            Capturas = Todas4, CapturaDeSerie = "2560x1440",
            TieneHome = false, HomeNativo4K = false, Menu = "pantalla de inicio", MenuPropio = true,
            Juegos120 = true, Juegos40 = true, ResolucionDinamica = true,
            BotonAceptar = "equis", BotonAtras = "círculo", BotonMenu = "PS", BotonCaptura = "Crear", BotonVista = "Crear", BotonOpciones = "Opciones",
            BotonesCara = new string[] { "equis", "círculo", "cuadrado", "triángulo" }, Gatillos = "L1/R1 y L2/R2",
            HdcpEnJuegos = true,
            ConsejoHdcp = "Sin imagen de la PS5 · desactiva el HDCP en Ajustes > Sistema > HDMI",
            // (al arrancar un juego con HDR o 120 Hz la PS5 cambia de modo de video: unos segundos sin señal)
            ApagadoPorMenu = false, EsperaSinSenalMinS = 8, EsperaNegroS = 30, Reposo = "modo de reposo",
            TextosApagado = new string[] { "modo de reposo", "apagar la ps5", "reiniciar la ps5", "rest mode", "turn off ps5", "restart ps5" },
            TextosMarca = new string[] { "ps5", "playstation 5", "playstation store", "playstation plus", "dualsense", "playstation" },
            AjustesVideo = "Ajustes > Pantalla y vídeo > Salida de vídeo", ConsejoVideo = "Resolución 2160p, salida de 120 Hz desactivada, HDR desactivado, rango RGB automático; HDCP desactivado (Ajustes > Sistema > HDMI)",
        };

        public static readonly ConsolaPerfil Ps4 = new ConsolaPerfil
        {
            Clave = "ps4", Nombre = "PlayStation 4", Corto = "PS4", Familia = "playstation",
            // (la PS4 normal y la Slim, 1080p; la Pro, 4K: sin saber cual, todas las capturas)
            SalidaMaxAlto = 2160, Da1440 = false, Da120 = false, Hdr = true, Hz5994 = true,
            Capturas = Todas4, CapturaDeSerie = "1920x1080",
            TieneHome = false, HomeNativo4K = false, Menu = "pantalla de inicio",
            Juegos120 = false, Juegos40 = false, ResolucionDinamica = true,
            BotonAceptar = "equis", BotonAtras = "círculo", BotonMenu = "PS", BotonCaptura = "SHARE", BotonVista = "SHARE", BotonOpciones = "OPTIONS",
            BotonesCara = new string[] { "equis", "círculo", "cuadrado", "triángulo" }, Gatillos = "L1/R1 y L2/R2",
            HdcpEnJuegos = true,
            ConsejoHdcp = "Sin imagen de la PS4 · desactiva el HDCP en Ajustes > Sistema > Activar HDCP",
            ApagadoPorMenu = false, EsperaSinSenalMinS = 6, EsperaNegroS = 30, Reposo = "modo de reposo",
            TextosApagado = new string[] { "modo de reposo", "apagar la ps4", "reiniciar la ps4", "rest mode", "turn off ps4", "restart ps4" },
            TextosMarca = new string[] { "ps4", "playstation 4", "playstation store", "playstation plus", "playstation" },
            AjustesVideo = "Ajustes > Sonido y pantalla > Ajustes de salida de vídeo", ConsejoVideo = "Resolución automática (2160p en la Pro, 1080p en las demás), HDR desactivado; HDCP desactivado (Ajustes > Sistema)",
        };

        public static readonly ConsolaPerfil XboxSeries = new ConsolaPerfil
        {
            Clave = "xboxseries", Nombre = "Xbox Series X|S", Corto = "Xbox Series", Familia = "xbox",
            SalidaMaxAlto = 2160, Da1440 = true, Da120 = true, Hdr = true, Hz5994 = true,
            Capturas = Todas4, CapturaDeSerie = "2560x1440",
            TieneHome = false, HomeNativo4K = false, Menu = "Inicio", MenuPropio = true,
            Juegos120 = true, Juegos40 = true, ResolucionDinamica = true,
            BotonAceptar = "A", BotonAtras = "B", BotonMenu = "Xbox", BotonCaptura = "Compartir", BotonVista = "Vista", BotonOpciones = "Menú",
            BotonesCara = new string[] { "A", "B", "X", "Y" }, Gatillos = "LB/RB y LT/RT",
            // (los juegos no llevan HDCP; solo las apps de video)
            HdcpEnJuegos = false, ConsejoHdcp = null,
            // (cambia de modo al entrar en un juego con HDR, Auto HDR o 120 Hz)
            ApagadoPorMenu = false, EsperaSinSenalMinS = 6, EsperaNegroS = 30, Reposo = "modo de suspensión",
            TextosApagado = new string[] { "apagar la consola", "reiniciar la consola", "apagar consola", "reiniciar consola", "turn off console", "restart console" },
            TextosMarca = new string[] { "xbox series", "game pass", "xbox" },
            // (con la GC573, 120 Hz solo a 1440p o 1080p y entonces sin HDR: el HDR de la Series solo va en el modo 4K)
            AjustesVideo = "Configuración > General > Opciones de TV y pantalla",
            ConsejoVideo = "Resolución 4K UHD, frecuencia 60 Hz, HDR10 y HDR automático desactivados, espacio de color Estándar",
            ConsejoAudio = "Configuración > General > Opciones de volumen y salida de audio: Estéreo sin comprimir",
        };

        public static readonly ConsolaPerfil XboxOne = new ConsolaPerfil
        {
            Clave = "xboxone", Nombre = "Xbox One", Corto = "Xbox One", Familia = "xbox",
            // (la One normal 1080p; la One S agranda a 4K; la One X, 4K; 120 Hz a 1080p/1440p en la S y la X desde 2018)
            SalidaMaxAlto = 2160, Da1440 = true, Da120 = true, Hdr = true, Hz5994 = true,
            Capturas = Todas4, CapturaDeSerie = "1920x1080",
            TieneHome = false, HomeNativo4K = false, Menu = "Inicio", MenuPropio = true,
            Juegos120 = false, Juegos40 = false, ResolucionDinamica = true,
            BotonAceptar = "A", BotonAtras = "B", BotonMenu = "Xbox", BotonCaptura = "Xbox", BotonVista = "Vista", BotonOpciones = "Menú",
            BotonesCara = new string[] { "A", "B", "X", "Y" }, Gatillos = "LB/RB y LT/RT",
            HdcpEnJuegos = false, ConsejoHdcp = null,
            ApagadoPorMenu = false, EsperaSinSenalMinS = 6, EsperaNegroS = 30, Reposo = "modo de suspensión",
            TextosApagado = new string[] { "apagar la consola", "reiniciar la consola", "apagar consola", "reiniciar consola", "turn off console", "restart console" },
            TextosMarca = new string[] { "xbox one", "game pass", "xbox" },
            AjustesVideo = "Configuración > General > Opciones de TV y pantalla",
            ConsejoVideo = "Resolución 4K UHD (One S y One X) o 1080p, frecuencia 60 Hz, HDR10 desactivado, espacio de color Estándar",
            ConsejoAudio = "Configuración > General > Opciones de volumen y salida de audio: Estéreo sin comprimir",
        };

        // Cualquier otra cosa por HDMI (un PC, la Steam Deck en su base, una consola antigua con escalador): nada de ninguna consola
        public static readonly ConsolaPerfil Otra = new ConsolaPerfil
        {
            Clave = "otra", Nombre = "Otra consola", Corto = "consola", Familia = "otra",
            SalidaMaxAlto = 2160, Da1440 = true, Da120 = true, Hdr = true, Hz5994 = true,
            Capturas = Todas4, CapturaDeSerie = "1920x1080",
            TieneHome = false, HomeNativo4K = false, Menu = "menú",
            Juegos120 = true, Juegos40 = true, ResolucionDinamica = false,
            BotonAceptar = "A", BotonAtras = "B", BotonMenu = "menú", BotonCaptura = "Captura", BotonVista = "Vista", BotonOpciones = "Menú",
            BotonesCara = new string[] { "A", "B", "X", "Y" }, Gatillos = "LB/RB y LT/RT",
            HdcpEnJuegos = false, ConsejoHdcp = null,
            ApagadoPorMenu = false, EsperaSinSenalMinS = 10, EsperaNegroS = 60, Reposo = "reposo",
            TextosApagado = new string[0], TextosMarca = new string[0],
            AjustesVideo = "los ajustes de imagen del aparato", ConsejoVideo = "4K, 1440p o 1080p a 60 Hz, sin HDR",
        };

        public static readonly ConsolaPerfil[] Todas = { Switch2, Switch1, Ps5, Ps4, XboxSeries, XboxOne, Otra };

        // ---------------- La elegida ----------------
        static readonly object sync = new object();
        static string elegida = Auto;               // lo del ini (consola=)
        static volatile ConsolaPerfil actual;       // null hasta Iniciar (Actual da entonces la de serie)
        static string origen = "de serie";
        static bool iniciada;

        // (las pruebas usan otra carpeta: nada en games\)
        static string carpeta = Path.Combine(Paths.Datos, "games");
        static string ArchivoUltima { get { return Path.Combine(carpeta, "console.txt"); } }
        static string ArchivoHomeRef { get { return Path.Combine(carpeta, "home_ref.txt"); } }

        // Cambio de consola (con auto): la de antes, la nueva y por que. Lo avisa la app (log, aviso, perfil de la consola)
        public static event Action<ConsolaPerfil, ConsolaPerfil, string> Cambiada;

        // Lo que pone el ini: auto (tambien vacio) o una clave. Devuelve false si no la conoce (se queda auto)
        public static bool Configurar(string val)
        {
            string v = (val ?? "").Trim().ToLowerInvariant();
            if (v.Length == 0 || v == Auto) { lock (sync) { elegida = Auto; } return true; }
            ConsolaPerfil p = Buscar(v);
            if (p == null)
            {
                Log.Write("Aviso: consola=" + val + " no es ninguna conocida (auto, switch2, switch1, ps5, ps4, xboxseries, xboxone u otra): se usa auto");
                lock (sync) { elegida = Auto; }
                return false;
            }
            lock (sync)
            {
                elegida = p.Clave;
                if (iniciada) { actual = p; origen = "switch2.ini"; }
            }
            return true;
        }

        public static string Elegida { get { lock (sync) return elegida; } }
        public static bool Fija { get { lock (sync) return elegida != Auto; } }
        public static string Origen { get { lock (sync) return origen; } }

        // La consola con la que se trabaja (nunca null)
        public static ConsolaPerfil Actual
        {
            get
            {
                ConsolaPerfil a = actual;
                if (a != null) return a;
                lock (sync) { if (actual == null) IniciarLocked(); return actual; }
            }
        }

        // Al abrir la app (tras leer el ini): la del ini, o con auto la ultima vista. Devuelve la linea para el log
        public static string Iniciar()
        {
            lock (sync) { IniciarLocked(); return LineaLog(); }
        }

        static void IniciarLocked()
        {
            iniciada = true;
            guardada = null;
            if (elegida != Auto) { actual = Buscar(elegida) ?? Switch2; origen = "switch2.ini"; return; }
            ConsolaPerfil last = null;
            try { if (File.Exists(ArchivoUltima)) { string g = File.ReadAllText(ArchivoUltima).Trim(); last = Buscar(g); guardada = g; } }
            catch { }
            if (last != null) { actual = last; origen = "la ultima vista (games\\console.txt)"; return; }
            // Sin historia: la Switch 2 si ya se vio su HOME en este PC (la app era solo para ella), si no ninguna en concreto
            bool sw2 = false;
            try { sw2 = File.Exists(ArchivoHomeRef); } catch { }
            actual = sw2 ? Switch2 : Otra;
            origen = sw2 ? "de serie (ya se vio el HOME de la Switch 2 en este PC)" : "de serie (sin saber cual: nada de ninguna consola)";
        }

        public static string LineaLog()
        {
            ConsolaPerfil a = actual ?? Switch2;
            return "Consola: " + a.Corto + " (" + (elegida == Auto ? "consola=auto, " : "") + origen + "). " + a.Resumen() + ApagadoTexto(a);
        }

        // ---------------- Apagado sin que un perfil equivocado cierre la app (20-11, agente CONSOLA) ----------------
        // El 20-11 (04:33 y 04:35) consola=auto tomo el menu de la PS5 por la guia de la Xbox: con el perfil de la Xbox, 30 s de negro y
        // silencio (la PS5 los da con el HDCP de una app de video o en una carga) contaban como consola apagada y se cerraba la app, y
        // "No Signal" al cambiar de modo de video al arrancar un juego (PS5: unos segundos) esperaba 6 s y no 8 (cierre por "No Signal" =
        // apagar_pc). Ahora, con consola=auto y una consola SIN PROBAR (Xbox, PS4, Switch 1) u "otra" elegida sola, detras puede haber
        // otra: lo mas prudente de todas. Con consola=<una> en el ini, lo suyo (lo ha dicho el usuario); con la Switch 2 y la PS5, lo suyo
        public static bool ApagadoPrudente(ConsolaPerfil c) { return c != null && !Fija && !c.Probada; }

        // Negro y silencio seguidos que cuentan como apagada (black_secs del script de mpv y del motor). PlayStation y prudente: nunca
        public static double NegroApagadoS(ConsolaPerfil c)
        {
            if (c == null) c = Actual;
            return c.HdcpEnJuegos || ApagadoPrudente(c) ? 86400 : Math.Max(10, c.EsperaNegroS);
        }

        // "No Signal" jugando: como poco esto (con ApagadoPorMenuSeguro, lo del ini tal cual). Prudente: la espera mas larga de todas
        public static double EsperaSinSenalMinS(ConsolaPerfil c)
        {
            if (c == null) c = Actual;
            if (!ApagadoPrudente(c)) return c.EsperaSinSenalMinS;
            double m = 0;
            foreach (ConsolaPerfil p in Todas) m = Math.Max(m, p.EsperaSinSenalMinS);
            return m;
        }

        public static bool ApagadoPorMenuSeguro(ConsolaPerfil c)
        {
            if (c == null) c = Actual;
            return c.ApagadoPorMenu && !ApagadoPrudente(c);
        }

        static string ApagadoTexto(ConsolaPerfil c)
        {
            return ApagadoPrudente(c) ? "; apagado prudente (consola=auto y " + c.Corto + " sin probar: sin cierre por negro y silencio, No Signal jugando como poco " +
                                        Fmt(EsperaSinSenalMinS(c)) + " s)" : "";
        }

        static string Fmt(double v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }

        // Una clave del ini o un nombre ("PS5", "Switch 2", "Series X"...): el perfil o null
        public static ConsolaPerfil Buscar(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            StringBuilder sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            string k = sb.ToString();
            foreach (ConsolaPerfil p in Todas) if (p.Clave == k) return p;
            switch (k)
            {
                case "ns2": case "nintendoswitch2": case "switch2": return Switch2;
                case "switch": case "ns": case "ns1": case "nintendoswitch": case "nintendoswitch1": case "switcholed": case "switchlite": return Switch1;
                case "playstation5": case "ps5pro": case "ps5slim": return Ps5;
                case "playstation4": case "ps4pro": case "ps4slim": return Ps4;
                case "xbox": case "series": case "seriesx": case "seriess": case "xboxseriesx": case "xboxseriess": case "xsx": case "xss": case "xboxseriesxs": return XboxSeries;
                case "one": case "xboxones": case "xboxonex": case "onex": case "ones": case "xb1": return XboxOne;
                case "pc": case "steamdeck": case "deck": case "retro": case "generica": case "generico": case "ninguna": return Otra;
            }
            return null;
        }

        // ---------------- Deteccion (con consola=auto) ----------------
        // Pistas con peso, que se olvidan con el tiempo (media vida de 10 min): cambia a una consola cuando su puntuacion llega a
        // PuntosCambio y dobla a la siguiente (la puesta incluida). Con consola=<una> no se hace nada (se registra la pista en el log una
        // vez, por si el ini esta mal).
        // 20-11 (CONSOLA): el 20-11 a las 04:33 y 04:35 la PS5 se tomo por la Xbox: con la PS5 puesta, cada captura en la que su lector no
        // leia su hora (la pantalla de inicio con el avatar junto a la hora: OCR "3C) 2:42"; algo a su derecha; un fundido) iba a buscar
        // la de la Xbox en TODA la franja de arriba, que incluye el sitio de la de la PS5: su propia hora daba 0,6 a la Xbox cada 5 s y
        // nada a la PS5 (su menu reconocido no sumaba), y en 5 pistas se cambiaba a un perfil SIN PROBAR y se guardaba en console.txt.
        // Ahora:
        //  - Cada pista es PROPIA (algo que solo da esa consola: su HOME con la referencia, su hora en su sitio exacto, su marca o sus
        //    pantallas en el texto) o de APOYO (la hora en la franja de arriba, "PlayStation" o "Xbox" sueltas, la cadencia de 40...).
        //    Las de apoyo suman, pero solas nunca cambian la consola.
        //  - Para cambiar hacen falta pruebas propias de la nueva repetidas en el tiempo (Confirmada: 2 con 5 s entre la primera y la
        //    ultima; a una SIN PROBAR desde una probada, 3 en 20 s) en los ultimos 10 min.
        //  - Ante la duda gana la puesta: su menu visto con su lector la refuerza (VistoMenuPropio, solo las probadas) y, si lo ha visto
        //    hace menos de 20 s, solo una prueba concluyente (el HOME de la Switch 2 con su referencia) la cambia.
        //  - games\console.txt solo se escribe con la consola confirmada (al cambiar, o la puesta con sus pruebas propias)
        public const double PuntosCambio = 3.0;
        const double MediaVidaS = 600;
        public const double SeparacionS = 5;            // 2 pruebas propias, con esto como poco entre la primera y la ultima
        public const double SeparacionSinProbarS = 20;  // de una probada a una SIN PROBAR: 3 pruebas propias en esto como poco
        public const double VentanaPropiasS = 600;      // las pruebas propias de hace mas de 10 min no cuentan
        public const double ActualRecienteS = 20;       // la puesta ha visto su propio menu hace menos de esto: gana
        static readonly Dictionary<string, double> puntos = new Dictionary<string, double>();
        static readonly Dictionary<string, List<DateTime>> propias = new Dictionary<string, List<DateTime>>();
        static readonly Dictionary<string, DateTime> rechazoLog = new Dictionary<string, DateTime>();
        static DateTime puntosAt = DateTime.MinValue;
        static string ultimaPistaLog;
        static string guardada;                         // lo que hay en games\console.txt (leido al abrir o escrito despues)
        public static volatile Func<DateTime> Reloj; // (pruebas: un reloj falso)

        static DateTime Ahora() { Func<DateTime> r = Reloj; return r != null ? r() : DateTime.Now; }

        static void Olvidar(DateTime now)
        {
            if (puntosAt != DateTime.MinValue)
            {
                double k = Math.Pow(0.5, Math.Max(0, (now - puntosAt).TotalSeconds) / MediaVidaS);
                List<string> keys = new List<string>(puntos.Keys);
                foreach (string c in keys) puntos[c] = puntos[c] * k;
            }
            puntosAt = now;
        }

        // Una pista a favor de una consola (o de dos, la segunda con otro peso: "PlayStation" vale para la PS5 y la PS4): suma su
        // peso a cada una y resta el mayor a las demas (solo hay una consola). Con peso >= PuntosCambio es concluyente (el HOME de la
        // Switch 2 con su referencia): las demas a cero. propia = algo que solo da esa consola (la primera; la segunda nunca): cuenta
        // para Confirmada. Devuelve la consola nueva si con ella se cambia (null si no)
        public static ConsolaPerfil Pista(string porque, bool propia, string clave, double peso, string clave2 = null, double peso2 = 0)
        {
            ConsolaPerfil p = Buscar(clave), p2 = clave2 != null ? Buscar(clave2) : null;
            if (p == null || peso <= 0) return null;
            ConsolaPerfil antes = null, nueva = null;
            string why = null;
            lock (sync)
            {
                if (!iniciada) IniciarLocked();
                if (elegida != Auto)
                {
                    if (peso >= 1.5 && propia && p != actual && ultimaPistaLog != p.Clave)
                    {
                        ultimaPistaLog = p.Clave;
                        Log.Write("Consola: la imagen parece de " + p.Corto + " (" + porque + "), pero switch2.ini dice consola=" + elegida + ": se sigue con esa");
                    }
                    return null;
                }
                DateTime now = Ahora();
                Olvidar(now);
                if (propia) ApuntaPropia(p.Clave, now);
                double top = Math.Max(peso, p2 != null ? peso2 : 0);
                foreach (ConsolaPerfil q in Todas)
                {
                    if (q == Otra) continue;
                    double v;
                    puntos.TryGetValue(q.Clave, out v);
                    if (q == p) v = (top >= PuntosCambio ? Math.Max(v, 0) : v) + peso;
                    else if (q == p2) v += peso2;
                    else if (top >= PuntosCambio) v = Math.Min(v, 0);
                    else if (v > -1) v = Math.Max(-1, v - top); // (lo que va en contra, como mucho hasta -1: solo VistaSenal descarta del todo)
                    puntos[q.Clave] = Math.Max(-PuntosCambio, Math.Min(PuntosCambio * 3, v));
                }
                nueva = DecidirLocked(porque, now, peso >= PuntosCambio, out antes);
                why = porque;
            }
            Avisar(antes, nueva, why);
            return nueva;
        }

        // Con sync: una prueba propia de esta consola, ahora
        static void ApuntaPropia(string clave, DateTime now)
        {
            List<DateTime> l;
            if (!propias.TryGetValue(clave, out l)) propias[clave] = l = new List<DateTime>();
            l.Add(now);
            l.RemoveAll(delegate (DateTime t) { return (now - t).TotalSeconds > VentanaPropiasS || t > now; });
            if (l.Count > 32) l.RemoveRange(0, l.Count - 32);
        }

        // Con sync: las pruebas propias de esta consola de los ultimos 10 min (en orden)
        static List<DateTime> PropiasLocked(string clave, DateTime now)
        {
            List<DateTime> l, r = new List<DateTime>();
            if (propias.TryGetValue(clave, out l))
                foreach (DateTime t in l) if ((now - t).TotalSeconds <= VentanaPropiasS && t <= now) r.Add(t);
            return r;
        }

        // Con sync: ¿esta consola esta confirmada por sus pruebas propias, repetidas en el tiempo? (falta = lo que le falta, para el log)
        static bool ConfirmadaLocked(ConsolaPerfil c, DateTime now, out string falta)
        {
            falta = null;
            List<DateTime> l = PropiasLocked(c.Clave, now);
            int need = 2;
            double span = SeparacionS;
            if (!c.Probada && actual != null && actual.Probada && actual != c) { need = 3; span = SeparacionSinProbarS; }
            double got = l.Count > 1 ? (l[l.Count - 1] - l[0]).TotalSeconds : 0;
            if (l.Count >= need && got >= span) return true;
            falta = l.Count + " prueba" + (l.Count == 1 ? "" : "s") + " propia" + (l.Count == 1 ? "" : "s") + (l.Count > 1 ? " en " + Fmt(got) + " s" : "") +
                    " (hacen falta " + need + " en " + Fmt(span) + " s como poco" + (need > 2 ? ": " + c.Corto + " esta SIN PROBAR" : "") + ")";
            return false;
        }

        // (pruebas y log) ¿Confirmada la consola con sus pruebas propias?
        public static bool Confirmada(ConsolaPerfil c)
        {
            lock (sync) { string f; return ConfirmadaLocked(c, Ahora(), out f); }
        }

        // Con sync: ¿cambia la consola? La de mas puntos si llega a PuntosCambio, dobla a la siguiente (la puesta incluida) y esta
        // confirmada por sus pruebas propias; y si la puesta acaba de ver su propio menu, solo con una prueba concluyente. La confirmada
        // se recuerda (games\console.txt) para la proxima vez
        static ConsolaPerfil DecidirLocked(string porque, DateTime now, bool concluyente, out ConsolaPerfil antes)
        {
            antes = null;
            string best = null;
            double b1 = 0, b2 = 0;
            foreach (KeyValuePair<string, double> kv in puntos)
            {
                if (kv.Value > b1) { b2 = b1; b1 = kv.Value; best = kv.Key; }
                else if (kv.Value > b2) b2 = kv.Value;
            }
            if (best == null || b1 < PuntosCambio) return null;
            string falta;
            if (best == actual.Clave)
            {
                // (solo con sus pruebas propias: nunca se guarda una consola que no se ha visto de verdad; y sin leer el fichero cada vez:
                // la puesta se refuerza cada 5 s con su menu a la vista)
                if (guardada == best || !ConfirmadaLocked(actual, now, out falta)) return null;
                try
                {
                    if (!File.Exists(ArchivoUltima) || File.ReadAllText(ArchivoUltima).Trim() != best)
                        File.WriteAllText(ArchivoUltima, best, new UTF8Encoding(false));
                    guardada = best;
                }
                catch { }
                return null;
            }
            // (20-11: "dobla" con el igual incluido: las pistas de la Xbox dan siempre a la One la mitad que a la Series y con los
            // redondeos a veces no llegaba)
            if (b1 + 1e-6 < 2 * Math.Max(0, b2)) return null;
            ConsolaPerfil cand = Buscar(best);
            if (cand == null) return null;
            if (!ConfirmadaLocked(cand, now, out falta)) { RechazoLocked(cand, b1, porque, falta, now); return null; }
            if (!concluyente && actual != Otra)
            {
                List<DateTime> mias = PropiasLocked(actual.Clave, now);
                if (mias.Count > 0 && (now - mias[mias.Count - 1]).TotalSeconds < ActualRecienteS)
                {
                    RechazoLocked(cand, b1, porque, actual.Corto + " ha visto su propio menu hace " + Fmt((now - mias[mias.Count - 1]).TotalSeconds) + " s", now);
                    return null;
                }
            }
            antes = actual;
            actual = cand;
            origen = "vista en la imagen: " + porque;
            try { File.WriteAllText(ArchivoUltima, actual.Clave, new UTF8Encoding(false)); guardada = actual.Clave; } catch { }
            return actual;
        }

        // Con sync: un cambio que no se hace por falta de pruebas (al log, una vez por minuto y consola)
        static void RechazoLocked(ConsolaPerfil cand, double pts, string porque, string falta, DateTime now)
        {
            DateTime at;
            if (rechazoLog.TryGetValue(cand.Clave, out at) && (now - at).TotalSeconds < 60 && now >= at) return;
            rechazoLog[cand.Clave] = now;
            Log.Write("Consola: la imagen apunta a " + cand.Corto + " (" + Fmt(pts) + " puntos; lo ultimo: " + porque + "), pero sin cambiar: " + falta +
                      ". Se sigue con " + (actual == Otra ? "otra (ninguna en concreto)" : actual.Corto));
        }

        static void Avisar(ConsolaPerfil antes, ConsolaPerfil nueva, string why)
        {
            if (nueva == null) return;
            Log.Write("Consola: ahora " + nueva.Corto + " (antes " + (antes != null ? antes.Corto : "?") + "; " + why + "). " + nueva.Resumen() + ApagadoTexto(nueva));
            Action<ConsolaPerfil, ConsolaPerfil, string> h = Cambiada;
            if (h != null) { try { h(antes, nueva, why); } catch (Exception ex) { Log.Error("Consola: aviso del cambio", ex); } }
        }

        public static string PuntosTexto()
        {
            lock (sync)
            {
                Olvidar(Ahora());
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, double> kv in puntos)
                    sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(kv.Value.ToString("0.0", CultureInfo.InvariantCulture));
                return sb.Length > 0 ? sb.ToString() : "(ninguna pista)";
            }
        }

        // (pruebas)
        public static void Reiniciar(string ini)
        {
            lock (sync)
            {
                puntos.Clear(); propias.Clear(); rechazoLog.Clear(); puntosAt = DateTime.MinValue; ultimaPistaLog = null;
                iniciada = false; actual = null; elegida = Auto;
            }
            Configurar(ini);
        }

        // (pruebas) Otra carpeta para console.txt y home_ref.txt (null = games\); devuelve la de antes
        public static string CarpetaDePrueba(string dir)
        {
            lock (sync)
            {
                string old = carpeta;
                carpeta = dir ?? Path.Combine(Paths.Datos, "games");
                return old;
            }
        }

        // El HOME de Nintendo visto (AnalyzeGrab, al pasar al HOME). conRef = casaba con games\home_ref.txt (el de la Switch 2).
        // altoNativo = lo que da AltoNativo con la captura del HOME (0 si no se midio): 1080 o menos es la Switch 1
        public static ConsolaPerfil VistoHomeNintendo(bool conRef, int altoNativo)
        {
            if (conRef) return Pista("el HOME de la Switch 2", true, "switch2", PuntosCambio);
            string what = "un HOME de Nintendo" + (altoNativo > 0 ? " a " + altoNativo + "p" : "");
            if (altoNativo > 1080) return Pista(what, true, "switch2", 2);
            if (altoNativo > 0) return Pista(what, true, "switch1", 2);
            return Pista(what, false, "switch2", 0.5, "switch1", 0.5); // (sin medir: una de las dos)
        }

        // 19-11: el menu de la PS5 visto con otra consola puesta (src\MenuPs5.cs: su hora arriba a la derecha, la del PC, sin nada a su
        // derecha; no en el HOME de la Switch 2). Prueba propia de la PS5 (su sitio exacto, probado con la consola), no concluyente
        public static ConsolaPerfil VistoMenuPs5(string hora)
        {
            return Pista("el menu de la PS5 (la hora \"" + hora + "\" arriba a la derecha)", true, "ps5", 1.5, "ps4", 0.4);
        }

        // 19-11 (DETECCION): la hora del PC en la franja de arriba, fuera del sitio de la de la PS5 y sin la bateria de la Switch: el Inicio o
        // la guia de la Xbox (SIN PROBAR con la consola). 20-11 (CONSOLA): solo de apoyo (la PS4 y la PS5 en otras pantallas tambien ponen
        // la hora arriba): sola nunca cambia la consola; y con una PlayStation puesta no se mira (DetectorMenu.PistaReloj)
        public static ConsolaPerfil VistoMenuXbox(string hora)
        {
            return Pista("la hora \"" + hora + "\" arriba (el Inicio o la guia de la Xbox; de apoyo)", false, "xboxseries", 0.6, "xboxone", 0.3);
        }

        // 20-11 (CONSOLA): la consola puesta ha visto su propio menu con su lector (la PS5: su hora en su sitio exacto; MenuPs5Captura cada
        // 5 s como mucho). Solo las probadas con su menu propio: la Xbox lee la hora en toda la franja de arriba y tambien casaria con la
        // de la PS5. Prueba propia: la refuerza frente a las demas y, durante ActualRecienteS, solo una prueba concluyente la cambia
        public static ConsolaPerfil VistoMenuPropio(ConsolaPerfil c, string hora)
        {
            if (c == null || !c.Probada || !c.MenuPropio) return null;
            return Pista("su menu (la hora \"" + hora + "\" en su sitio)", true, c.Clave, 1.0);
        }

        // Texto de la pantalla leido por el OCR (cualquiera: pantallas de la consola, avisos, el menu). Solo palabras que delatan una
        // marca, y una pista por lectura. La marca escrita y las pantallas que solo tiene una son pruebas propias; "PlayStation", "Xbox",
        // "Game Pass" o "Nintendo" sueltas, de apoyo
        public static ConsolaPerfil VistoTexto(string ocr)
        {
            if (string.IsNullOrEmpty(ocr)) return null;
            string n = Normaliza(ocr);
            // (de mas concreta a menos: "nintendo switch 2" antes que "nintendo switch", "xbox series" antes que "xbox")
            if (Contiene(n, "nintendo switch 2") || Contiene(n, "switch 2 edition")) return Pista("\"Nintendo Switch 2\" en pantalla", true, "switch2", 1.5);
            if (Contiene(n, "ps5") || Contiene(n, "playstation 5") || Contiene(n, "dualsense")) return Pista("\"PS5\" en pantalla", true, "ps5", 1.5);
            if (Contiene(n, "ps4") || Contiene(n, "playstation 4") || Contiene(n, "dualshock")) return Pista("\"PS4\" en pantalla", true, "ps4", 1.5);
            if (Contiene(n, "xbox series")) return Pista("\"Xbox Series\" en pantalla", true, "xboxseries", 1.5);
            if (Contiene(n, "xbox one")) return Pista("\"Xbox One\" en pantalla", true, "xboxone", 1.5);
            if (Contiene(n, "playstation")) return Pista("\"PlayStation\" en pantalla", false, "ps5", 0.6, "ps4", 0.3);
            // 19-11 (DETECCION): pantallas que solo tiene una (sin la marca escrita)
            if (Contiene(n, "centro de control") || Contiene(n, "game base") || Contiene(n, "tu coleccion") || Contiene(n, "control center"))
                return Pista("una pantalla de la PS5 en pantalla", true, "ps5", 0.8);
            if (Contiene(n, "mis juegos y aplicaciones") || Contiene(n, "perfil y sistema") || Contiene(n, "fiesta y chats") || Contiene(n, "my games apps"))
                return Pista("una pantalla de la Xbox en pantalla", true, "xboxseries", 0.8, "xboxone", 0.4);
            if (Contiene(n, "xbox") || Contiene(n, "game pass")) return Pista("\"Xbox\" en pantalla", false, "xboxseries", 0.6, "xboxone", 0.3);
            if (Contiene(n, "nintendo switch online") || Contiene(n, "nintendo eshop") || Contiene(n, "nintendo switch"))
                return Pista("Nintendo en pantalla", false, "switch2", 0.4, "switch1", 0.4);
            return null;
        }

        // La señal de entrada de la capturadora, si algun dia se sabe leer (el SDK de AVerMedia la da; DirectShow y mpv no): las
        // consolas que no la pueden sacar, descartadas; si queda una sola de las de 1080p (la Switch 1), pista floja a su favor.
        // w x h, Hz (0 = sin saber), hdr. Devuelve la consola nueva si se cambia
        public static ConsolaPerfil VistaSenal(int w, int h, double hz, bool hdr)
        {
            string what = "la señal " + w + "x" + h + (hz > 0 ? " a " + hz.ToString("0.##", CultureInfo.InvariantCulture) + " Hz" : "") + (hdr ? " con HDR" : "");
            bool sw1 = false;
            ConsolaPerfil antes, nueva;
            lock (sync)
            {
                if (!iniciada) IniciarLocked();
                if (elegida != Auto) return null;
                DateTime now = Ahora();
                Olvidar(now);
                foreach (ConsolaPerfil p in Todas)
                {
                    if (p == Otra) continue;
                    bool no = (h > p.SalidaMaxAlto) || (h == 1440 && !p.Da1440) || (hz > 75 && !p.Da120) || (hdr && !p.Hdr);
                    if (no) puntos[p.Clave] = -PuntosCambio;
                    else if (p == Switch1) sw1 = true;
                }
                nueva = DecidirLocked(what, now, false, out antes);
            }
            Avisar(antes, nueva, what);
            if (nueva == null && sw1 && h > 0 && h <= 1080 && hz > 0 && hz < 75 && !hdr) nueva = Pista(what + " (lo de la Switch 1)", false, "switch1", 0.5);
            return nueva;
        }

        // Imagenes nuevas por segundo (las que cuenta el filtro de repetidas): "30", "40", "60" o "" (otra cosa: menus, cargas)
        public static string Cadencia(double nuevas)
        {
            if (nuevas >= 26 && nuevas <= 34) return "30";
            if (nuevas >= 36.5 && nuevas <= 43.5) return "40";
            if (nuevas >= 50) return "60";
            return "";
        }

        // Un juego a 40 seguido (solo existe con la consola a 120 Hz: PS5, Xbox Series): pista floja
        public static ConsolaPerfil VistaCadencia(double nuevas, double segundos)
        {
            if (Cadencia(nuevas) != "40" || segundos < 20) return null;
            return Pista("un juego a 40 imagenes por segundo", false, "ps5", 0.4, "xboxseries", 0.4);
        }

        // La resolucion de verdad de lo que se ve (en un menu: el texto y la interfaz van a la resolucion de salida de la consola; en
        // los juegos no vale, la consola los agranda por dentro). Lo que la capturadora (o la consola) agranda queda liso a la escala
        // del pixel: la diferencia segunda (laplaciano) baja mucho frente a la primera (el detalle). Medido el 18-11 con capturas del
        // HOME y de la biblioteca de la Switch 2 (scratchpad consolas\exp\nativo.cs), laplaciano / detalle en el centro de la imagen:
        //   tal cual 1,88-2,63 | agrandada 1,33-1,5 veces 1,25-1,51 | 2 veces o mas 0,90-1,06 (bilineal o bicubica) | reducida 2,1-2,4
        //   vecino mas cercano: pares de pixeles iguales en una fase (1,00) y no en la otra (0,3-0,4)
        // Devuelve la altura de verdad estimada (la de la captura si no se ve agrandada; 2/3 o la mitad si lo esta) o 0 si la imagen
        // tiene poco detalle para saberlo (un juego oscuro o liso: Ocarina a 1440p da 1,6 de detalle)
        public const double DetalleMin = 2.5;     // por debajo, no se sabe
        public const double LapNativa = 1.65;     // por encima, tal cual
        public const double LapDoble = 1.15;      // por debajo, agrandada 2 veces o mas
        public static int AltoNativo(Bitmap full)
        {
            double detalle, lap;
            return AltoNativo(full, out detalle, out lap);
        }

        public static int AltoNativo(Bitmap full, out double detalle, out double lapDet)
        {
            detalle = 0; lapDet = 0;
            if (full == null || full.Width < 64 || full.Height < 64) return 0;
            int W = full.Width, H = full.Height;
            Rectangle c = new Rectangle(W / 4, H / 4, W / 2, H / 2); // (el centro: la mitad de ancho y de alto)
            double[] l;
            int cw = c.Width, ch = c.Height;
            using (Bitmap crop = Copia(full, c)) l = Luma(crop);
            double sd = 0, sl = 0;
            int n = 0, eq0 = 0, eq1 = 0, np = 0;
            for (int y = 4; y < ch - 4; y++)
                for (int x = 4; x < cw - 4; x++)
                {
                    int i = y * cw + x;
                    sd += Math.Abs(l[i] - l[i + 1]) + Math.Abs(l[i] - l[i + cw]);
                    sl += Math.Abs(4 * l[i] - l[i - 1] - l[i + 1] - l[i - cw] - l[i + cw]);
                    n++;
                    if ((x & 1) == 0 && x + 2 < cw - 4)
                    {
                        if (Math.Abs(l[i] - l[i + 1]) < 0.5) eq0++;
                        if (Math.Abs(l[i + 1] - l[i + 2]) < 0.5) eq1++;
                        np++;
                    }
                }
            if (n == 0) return 0;
            detalle = sd / (2.0 * n);
            if (detalle < DetalleMin) return 0;
            lapDet = (sl / n) / detalle;
            double hi = np > 0 ? Math.Max(eq0, eq1) / (double)np : 0, lo = np > 0 ? Math.Min(eq0, eq1) / (double)np : 0;
            if (hi >= 0.9 && lo <= 0.6) return Estandar(H / 2);        // (vecino mas cercano, 2 veces)
            if (lapDet >= LapNativa) return H;
            if (lapDet < LapDoble) return Estandar(H / 2);
            return Estandar(H * 2 / 3);
        }

        // La altura de salida normal mas cercana (480, 720, 1080, 1440, 2160)
        static int Estandar(int h)
        {
            int[] std = { 480, 720, 1080, 1440, 2160 };
            int best = std[0];
            foreach (int s in std) if (Math.Abs(s - h) < Math.Abs(best - h)) best = s;
            return best;
        }
        static Bitmap Copia(Bitmap src, Rectangle r)
        {
            Bitmap b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(src, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
            }
            return b;
        }

        // (pruebas) Bajar con media y subir bilineal, como una capturadora o la consola
        static Bitmap Escala(Bitmap src, int w, int h)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY); // (sin el borde oscuro de GDI+)
                g.InterpolationMode = w < src.Width ? InterpolationMode.HighQualityBilinear : InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return b;
        }

        static double[] Luma(Bitmap b)
        {
            BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                byte[] px = new byte[d.Stride * b.Height];
                Marshal.Copy(d.Scan0, px, 0, px.Length);
                double[] l = new double[b.Width * b.Height];
                for (int y = 0; y < b.Height; y++)
                    for (int x = 0; x < b.Width; x++)
                    {
                        int o = y * d.Stride + x * 4;
                        l[y * b.Width + x] = (19 * px[o] + 183 * px[o + 1] + 54 * px[o + 2]) / 256.0;
                    }
                return l;
            }
            finally { b.UnlockBits(d); }
        }

        static string Normaliza(string s)
        {
            StringBuilder sb = new StringBuilder(" ");
            foreach (char c in DetectorMenu.Normal(s)) sb.Append(char.IsLetterOrDigit(c) ? c : ' '); // (19-11: sin tildes)
            sb.Append(' ');
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " +", " ");
        }

        static bool Contiene(string n, string palabras) { return n.Contains(" " + palabras + " "); }

        // ---------------- Prueba sin consola (--test-consolas en la compilacion de prueba; fase 2: en Program) ----------------
        public static string SelfTest(out int fallos)
        {
            StringBuilder sb = new StringBuilder();
            int f = 0;
            Action<string, bool> check = delegate (string what, bool ok)
            {
                sb.Append(ok ? "OK    " : "FALLO ").Append(what).Append("\r\n");
                if (!ok) f++;
            };
            Action<string> saved = Log.Hook;
            List<string> lines = new List<string>();
            Log.Hook = delegate (string m) { lines.Add(m); };
            string carpetaReal = carpeta;
            string tmp = Path.Combine(Path.GetTempPath(), "prisma_test_consolas_" + System.Diagnostics.Process.GetCurrentProcess().Id);
            Action<ConsolaPerfil, ConsolaPerfil, string> onChange = null;
            try
            {
                // Con la carpeta de verdad (games\, solo leyendo): consola=switch2 y consola=auto en este PC (con games\home_ref.txt y sin
                // games\console.txt) dan la Switch 2 con su HOME, como antes de que hubiera consolas
                bool realLast = File.Exists(ArchivoUltima), realRef = File.Exists(ArchivoHomeRef);
                Reiniciar("switch2");
                check("consola=switch2 -> Switch 2 con el HOME a 4K nativo y los juegos por su icono", Actual == Switch2 && Actual.TieneHome && Actual.HomeNativo4K && Actual.ApagadoPorMenu);
                Reiniciar("auto");
                ConsolaPerfil enPc = Actual;
                check("consola=auto en este PC (games\\console.txt " + (realLast ? "si" : "no") + ", home_ref.txt " + (realRef ? "si" : "no") + ") -> " + enPc.Clave +
                      " (" + Origen + ")", realLast || (realRef ? enPc == Switch2 && enPc.TieneHome : enPc == Otra));
                Directory.CreateDirectory(tmp);
                foreach (string old in Directory.GetFiles(tmp)) File.Delete(old);
                lock (sync) carpeta = tmp; // (nada en games\)
                check("Buscar(\"PS5\") = ps5", Buscar("PS5") == Ps5);
                check("Buscar(\"Switch 2\") = switch2", Buscar("Switch 2") == Switch2);
                check("Buscar(\"Xbox Series X\") = xboxseries", Buscar("Xbox Series X") == XboxSeries);
                check("Buscar(\"one x\") = xboxone", Buscar("one x") == XboxOne);
                check("Buscar(\"Steam Deck\") = otra", Buscar("Steam Deck") == Otra);
                check("Buscar(\"gamecube\") = null", Buscar("gamecube") == null);
                foreach (ConsolaPerfil p in Todas)
                {
                    bool ok = p.Capturas != null && p.Capturas.Length > 0 && p.CapturaUtil(p.CapturaDeSerie) && p.BotonesCara.Length == 4 &&
                              p.Nombre != null && p.Menu != null && (p.HdcpEnJuegos == (p.ConsejoHdcp != null)) && (!p.HomeNativo4K || p.TieneHome);
                    check("Perfil " + p.Clave + " completo: " + p.Resumen(), ok);
                }
                check("Switch 1: 4K no tiene sentido, se queda en 1080p", !Switch1.CapturaUtil("3840x2160") && Switch1.CapturaCercana("3840x2160") == "1920x1080" &&
                      Switch1.AvisoCaptura("3840x2160", "4K nativo") == "Captura 4K nativo · la consola da como mucho 1080p");
                check("PS5: 1440p tiene sentido", Ps5.CapturaUtil("2560x1440") && Ps5.AvisoCaptura("2560x1440", "1440p") == null);
                check("Cadencias 30/40/60", Cadencia(29.97) == "30" && Cadencia(40) == "40" && Cadencia(59.9) == "60" && Cadencia(45) == "");

                // Deteccion con un reloj falso
                DateTime t0 = new DateTime(2026, 11, 18, 12, 0, 0);
                DateTime now = t0;
                Reloj = delegate { return now; };
                Reiniciar("ps5");
                check("consola=ps5 manda", Actual == Ps5 && Fija);
                check("con consola=ps5 el HOME de la Switch 2 no cambia nada", VistoHomeNintendo(true, 2160) == null && Actual == Ps5);
                Reiniciar("auto");
                lock (sync) { actual = Switch2; iniciada = true; origen = "prueba"; }
                ConsolaPerfil got = null;
                onChange = delegate (ConsolaPerfil a, ConsolaPerfil b, string w) { got = b; };
                Cambiada += onChange;
                check("auto: una sola palabra \"PlayStation\" no basta", VistoTexto("Explorar PlayStation Store Juegos") == null && Actual == Switch2);
                // (20-11, CONSOLA: las pruebas propias, repetidas en el tiempo: 2 con 5 s entre la primera y la ultima)
                check("auto: \"PS5\" dos veces en el mismo segundo no basta", VistoTexto("Apagar la PS5") == null && VistoTexto("Reiniciar la PS5") == null && Actual == Switch2);
                now = now.AddSeconds(6);
                check("auto: \"PS5\" otra vez a los 6 s -> PS5", VistoTexto("Reiniciar la PS5 · Entrar en modo de reposo") == Ps5 && Actual == Ps5 && got == Ps5);
                now = now.AddSeconds(1);
                check("auto: el HOME de la Switch 2 (con su referencia) una vez: aun no", VistoHomeNintendo(true, 2160) == null && Actual == Ps5);
                now = now.AddSeconds(6);
                check("auto: ...y otra a los 6 s (concluyente, aunque la PS5 se viera hace nada) -> Switch 2", VistoHomeNintendo(true, 2160) == Switch2 && Actual == Switch2);
                now = now.AddMinutes(40); // (las pistas de antes ya casi no cuentan)
                VistoHomeNintendo(false, 1080);
                now = now.AddSeconds(10);
                bool sw1Antes = VistoHomeNintendo(false, 1080) == null && Actual == Switch2;
                now = now.AddSeconds(10);
                check("auto: un HOME de Nintendo a 1080p sin la referencia -> Switch 1 (SIN PROBAR: 3 veces en 20 s, no antes)",
                      sw1Antes && VistoHomeNintendo(false, 1080) == Switch1 && Actual == Switch1);
                now = now.AddMinutes(40); // (otra sesion, mas tarde, con otra consola)
                VistoTexto("Xbox Series X");
                now = now.AddSeconds(3);
                bool xsAntes = VistoTexto("Mis juegos Game Pass") == null;
                now = now.AddSeconds(3);
                check("auto: \"Xbox Series\", \"Game Pass\" y \"Xbox Series\" en 6 s -> Xbox Series", xsAntes && VistoTexto("Xbox Series S") == XboxSeries);
                check("auto: la señal 1440p a 120 Hz descarta la Switch 1 y la PS4", VistaSenal(2560, 1440, 119.88, false) == null && Actual == XboxSeries);
                Reiniciar("auto");
                check("auto: al abrir, la ultima vista (console.txt = xboxseries)", Actual == XboxSeries);
                File.Delete(Path.Combine(tmp, "console.txt"));
                Reiniciar("auto");
                check("auto: sin console.txt ni el HOME de la Switch 2 visto nunca -> otra", Actual == Otra);
                File.WriteAllText(Path.Combine(tmp, "home_ref.txt"), "1,2,3");
                Reiniciar("auto");
                check("auto: sin console.txt pero con games\\home_ref.txt -> switch2 (como antes)", Actual == Switch2);
                Reiniciar("xbox one x");
                check("consola=xbox one x -> xboxone", Actual == XboxOne && Elegida == "xboxone");
                Reiniciar("gamecube");
                check("consola=gamecube -> auto (con aviso en el log)", Elegida == Auto && lines.Count > 0 && lines[lines.Count - 1].Contains("gamecube"));
                check("Visible = \"consola\"", Visible == "consola" && TextoEnciende == "Enciende la consola");
                PruebaCambios(check, tmp, delegate (DateTime t) { now = t; }, delegate { return now; }, delegate { ConsolaPerfil g = got; got = null; return g; });

                // La resolucion de verdad: capturas del HOME y de la biblioteca de la Switch 2 (1080p) tal cual -> 1080; de 720p agrandada
                // a 1080p -> 720; agrandada a 4K (una consola de 1080p con la captura de 4K) -> 1080; con el vecino mas cercano -> 1080
                string[] imgs = { @"games\test_biblioteca\bib_02_rocket.png", @"games\test_biblioteca\bib_05_ff7.png", @"games\test_biblioteca\bib_08_hollow.png",
                                  @"games\test_biblioteca\bib_01_re_debajo.png" };
                foreach (string rel in imgs)
                {
                    string path = Path.Combine(Paths.AppDir, rel);
                    if (!File.Exists(path)) { sb.Append("(sin ").Append(rel).Append(")\r\n"); continue; }
                    using (Bitmap img = new Bitmap(path))
                    using (Bitmap full = Copia(img, new Rectangle(0, 0, img.Width, img.Height)))
                    {
                        string name = Path.GetFileName(rel);
                        double det, lap;
                        int a1 = AltoNativo(full, out det, out lap);
                        check(name + " tal cual (" + full.Height + "p): " + a1 + "p (detalle " + det.ToString("0.0", CultureInfo.InvariantCulture) + ", laplaciano/detalle " + lap.ToString("0.00", CultureInfo.InvariantCulture) + ")", a1 == full.Height);
                        using (Bitmap d = Escala(full, 1280, 720))
                        using (Bitmap u = Escala(d, full.Width, full.Height))
                        {
                            int a2 = AltoNativo(u, out det, out lap);
                            check(name + " de 720p agrandada a 1080p: " + a2 + "p (" + lap.ToString("0.00", CultureInfo.InvariantCulture) + ")", a2 == 720);
                        }
                        using (Bitmap u4 = Escala(full, 3840, 2160))
                        {
                            int a3 = AltoNativo(u4, out det, out lap);
                            check(name + " agrandada a 4K: " + a3 + "p (" + lap.ToString("0.00", CultureInfo.InvariantCulture) + ")", a3 == 1080);
                        }
                        using (Bitmap nn = new Bitmap(3840, 2160, PixelFormat.Format32bppRgb))
                        {
                            using (Graphics g = Graphics.FromImage(nn))
                            {
                                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                                g.PixelOffsetMode = PixelOffsetMode.Half;
                                g.DrawImage(full, new Rectangle(0, 0, 3840, 2160));
                            }
                            int a4 = AltoNativo(nn, out det, out lap);
                            check(name + " agrandada a 4K con el vecino mas cercano: " + a4 + "p", a4 == 1080);
                        }
                        using (Bitmap dn = Escala(full, 1280, 720))
                        {
                            int a5 = AltoNativo(dn, out det, out lap);
                            check(name + " reducida a 720p (una captura menor que la salida): " + a5 + "p", a5 == 720);
                        }
                    }
                }
                using (Bitmap flat = new Bitmap(1920, 1080, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(flat)) g.Clear(Color.FromArgb(20, 20, 24));
                    check("Imagen lisa: no se sabe (0)", AltoNativo(flat) == 0);
                }
            }            catch (Exception ex) { check("excepcion: " + ex, false); }
            finally
            {
                Reloj = null;
                if (onChange != null) Cambiada -= onChange;
                Log.Hook = saved;
                lock (sync) carpeta = carpetaReal;
                try { Directory.Delete(tmp, true); } catch { }
                Reiniciar("auto");
            }
            sb.Append("\r\n-- Lo que se habria escrito en el log --\r\n");
            foreach (string l in lines) sb.Append(l).Append("\r\n");
            fallos = f;
            sb.Insert(0, "Prueba de Consolas.cs: " + f + " fallos\r\n\r\n");
            return sb.ToString();
        }

        // 20-11 (CONSOLA): lo del 20-11 04:33 (la PS5 tomada por la Xbox) y el apagado con un perfil que puede ser el equivocado. Con la
        // carpeta de prueba (tmp) y el reloj falso de SelfTest
        static void PruebaCambios(Action<string, bool> check, string tmp, Action<DateTime> ponHora, Func<DateTime> hora, Func<ConsolaPerfil> cambio)
        {
            string txt = Path.Combine(tmp, "console.txt");
            Func<string> enTxt = delegate { try { return File.Exists(txt) ? File.ReadAllText(txt).Trim() : "(nada)"; } catch { return "?"; } };
            DateTime t = hora().AddHours(5);
            ponHora(t);

            // 1. La PS5 puesta (console.txt = ps5) y su propia hora leida en la franja de arriba (lo que pasaba: su lector no la leia por el
            //    avatar junto a ella y la de la Xbox si), cada 5 s durante 10 min: sigue la PS5 y console.txt no cambia
            File.WriteAllText(txt, "ps5");
            Reiniciar("auto");
            cambio();
            bool ok = Actual == Ps5;
            for (int i = 0; i < 120; i++) { t = t.AddSeconds(5); ponHora(t); if (VistoMenuXbox("4:33") != null) ok = false; }
            check("20-11: PS5 puesta y la hora en la franja de arriba (la de la Xbox) 120 veces en 10 min -> sigue la PS5, console.txt = " + enTxt(),
                  ok && Actual == Ps5 && cambio() == null && enTxt() == "ps5");

            // 2. Lo mismo con su menu reconocido de vez en cuando y alguna palabra suelta ("Xbox", "Game Pass"): sigue la PS5
            ok = true;
            for (int i = 0; i < 60; i++)
            {
                t = t.AddSeconds(5); ponHora(t);
                if (i % 6 == 0 && VistoMenuPropio(Ps5, "4:34") != null) ok = false;
                if (VistoMenuXbox("4:34") != null) ok = false;
                if (i % 10 == 5 && VistoTexto("Xbox Game Pass Ultimate") != null) ok = false;
            }
            check("20-11: PS5 puesta, su menu, la hora en la franja y \"Xbox\" / \"Game Pass\" sueltas 5 min -> sigue la PS5", ok && Actual == Ps5 && cambio() == null);

            // 3. Con su menu visto hace nada, ni la marca de otra dos veces la cambia (ante la duda gana la puesta)
            t = t.AddSeconds(5); ponHora(t);
            VistoMenuPropio(Ps5, "4:40");
            VistoTexto("Xbox Series X");
            t = t.AddSeconds(6); ponHora(t);
            ok = VistoTexto("Xbox Series X") == null && Actual == Ps5;
            check("20-11: PS5 con su menu visto hace 6 s y \"Xbox Series\" dos veces en 6 s -> sigue la PS5", ok && cambio() == null && enTxt() == "ps5");

            // 4. Una Xbox de verdad en lugar de la PS5: sus pantallas ("Mis juegos y aplicaciones") cada 5 s, sin el menu de la PS5: a la Xbox
            //    (SIN PROBAR) con 3 pruebas propias en 20 s como poco, no antes
            t = t.AddMinutes(30); ponHora(t);
            Reiniciar("auto"); // (console.txt = ps5)
            cambio();
            DateTime t0 = t;
            ConsolaPerfil nueva = null;
            double cuando = -1;
            for (int i = 0; i < 24 && nueva == null; i++)
            {
                nueva = VistoTexto("Mis juegos y aplicaciones · Microsoft Store");
                if (nueva == null) VistoMenuXbox("5:10");
                if (nueva != null) cuando = (t - t0).TotalSeconds;
                t = t.AddSeconds(5); ponHora(t);
            }
            check("20-11: PS5 puesta y una Xbox de verdad (sus pantallas cada 5 s) -> Xbox Series a los " + cuando + " s (no antes de 20 s), console.txt = " + enTxt(),
                  nueva == XboxSeries && cuando >= SeparacionSinProbarS && enTxt() == "xboxseries");
            check("20-11: con la Xbox elegida sola (SIN PROBAR): apagado prudente (negro y silencio nunca, No Signal jugando " + Fmt(EsperaSinSenalMinS(XboxSeries)) + " s)",
                  ApagadoPrudente(XboxSeries) && NegroApagadoS(XboxSeries) >= 86400 && EsperaSinSenalMinS(XboxSeries) >= 10 && !ApagadoPorMenuSeguro(XboxSeries));

            // 5. Lo del 20-11 05:22: la Xbox puesta por error (console.txt = xboxseries) con la PS5 detras: su menu en su sitio cada 5 s
            //    (MenuPs5Pista) -> la PS5 a la tercera (1,5 cada una: 3 puntos) y console.txt = ps5
            File.WriteAllText(txt, "xboxseries");
            t = t.AddMinutes(30); ponHora(t);
            Reiniciar("auto");
            cambio();
            bool antesXbox = Actual == XboxSeries;
            VistoMenuPs5("5:22");
            t = t.AddSeconds(5); ponHora(t);
            bool dos = VistoMenuPs5("5:22") == null && Actual == XboxSeries;
            t = t.AddSeconds(5); ponHora(t);
            ok = antesXbox && dos && VistoMenuPs5("5:22") == Ps5 && Actual == Ps5;
            check("20-11: Xbox puesta por error y el menu de la PS5 en su sitio cada 5 s -> PS5 a los 10 s, console.txt = " + enTxt(), ok && enTxt() == "ps5");

            // 6. Sin console.txt ni home_ref.txt (otra) y solo la hora en la franja 10 min: nunca a la Xbox (SIN PROBAR) por la hora;
            //    console.txt sin escribir
            File.Delete(txt);
            File.Delete(Path.Combine(tmp, "home_ref.txt"));
            t = t.AddMinutes(30); ponHora(t);
            Reiniciar("auto");
            cambio();
            ok = Actual == Otra;
            for (int i = 0; i < 120; i++) { t = t.AddSeconds(5); ponHora(t); if (VistoMenuXbox("6:00") != null) ok = false; }
            check("20-11: otra (sin console.txt) y solo la hora en la franja 120 veces -> sigue otra, console.txt " + enTxt(), ok && Actual == Otra && enTxt() == "(nada)");

            // 7. Una sola prueba propia no escribe console.txt
            VistoMenuPs5("6:10");
            check("20-11: una sola vez el menu de la PS5 (sin console.txt) -> sigue otra y console.txt sin escribir", Actual == Otra && enTxt() == "(nada)");

            // 8. El apagado de cada una (consola=auto salvo lo dicho)
            check("Apagado PS5 (auto): negro y silencio nunca, No Signal 8 s", !ApagadoPrudente(Ps5) && NegroApagadoS(Ps5) >= 86400 && EsperaSinSenalMinS(Ps5) == 8);
            check("Apagado Switch 2 (auto): negro y silencio 30 s, el de su menu de apagado", NegroApagadoS(Switch2) == 30 && ApagadoPorMenuSeguro(Switch2));
            check("Apagado otra (auto): negro y silencio nunca, No Signal 10 s", NegroApagadoS(Otra) >= 86400 && EsperaSinSenalMinS(Otra) == 10);
            Reiniciar("xboxseries");
            check("Apagado con consola=xboxseries en el ini: lo suyo (negro 30 s, No Signal 6 s)", !ApagadoPrudente(XboxSeries) && NegroApagadoS(XboxSeries) == 30 && EsperaSinSenalMinS(XboxSeries) == 6);
            Reiniciar("auto");
        }
    }
}
