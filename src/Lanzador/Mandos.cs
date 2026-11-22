// Mandos.cs - Entrada de mandos propia de la app (18-11, MANDOS: el usuario: "que el mando no funcione con Joyxoff, que la app tenga
// la habilidad de recibir esos inputs de los mandos").
//
//   PadHub  : XInput (Xbox y mandos en modo X-input; con XInputGetStateEx tambien el boton Guia) + HID por Raw Input (DualSense,
//             DualShock 4, Mando Pro de Switch / Joy-Con, Mando Pro de Switch 2, 8BitDo y GameSir en modo D-input, genericos con el
//             descriptor HID) + winmm (joyGetPosEx) como ultimo recurso para lo que no ve nadie mas. Varios mandos a la vez, conexion
//             en caliente (WM_INPUT_DEVICE_CHANGE y la interfaz XUSB), un modelo de botones unico (PadBtn: los bits bajos son los de
//             XInput, como los usa GamepadLoop) y el tipo de mando para nombrar sus botones (PadNames).
//             El boton de captura del GameSir (su receptor lo manda como teclado: Win derecha + Impr Pant) se ve por Raw Input con el
//             dispositivo (VID_3537): respaldo del gancho del teclado (KeyHook), que sigue quitandoselo a Windows con la imagen delante.
//   PcMode  : el "modo PC": sin Joyxoff, el mando maneja el PC fuera de la imagen de la consola con lo que tenia el perfil de Joyxoff
//             del usuario (C:\ProgramData\Joyxoff\bindings.bin, leido el 18-11): stick izquierdo = raton, stick derecho = rueda,
//             A = clic izquierdo, X = clic derecho, R3 = clic central, B = Esc, cruceta = flechas, Vista = Alt, Menu = F4 (las dos = Alt+F4),
//             L3 = teclado en pantalla, Guia = abrir un programa (Playnite en pantalla completa). En pausa en pantalla completa (como el
//             "auto-desactivar" de Joyxoff, salvo los programas de mando_pc_pantalla_completa) y con Joyxoff abierto (no se duplica).
//   PadTest : --test-mandos=<segundos>;<archivo>: lista los mandos, apunta lo pulsado y el estado del modo PC (sin mover nada).
//
// Coste: XInput se lee cada 50 ms (como antes; los huecos vacios cada 3 s o al conectar algo); lo HID llega solo (sin sondeo) a un
// hilo con una ventana de solo mensajes; el modo PC solo mira la ventana de delante cuando se toca el mando y solo va a 8 ms mientras
// se mueve un stick.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ConsoleEnhancer
{
    // Botones del modelo unico. Los bits bajos son los de XInput (GamepadLoop los usa tal cual); 0x0400 / 0x0800 quedan para los
    // gatillos (L2 / R2 con histeresis en GamepadLoop: aqui los gatillos van en LT / RT de 0 a 255). Lo demas, lo que XInput no ve
    static class PadBtn
    {
        public const uint Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008, Start = 0x0010, View = 0x0020,
                          L3 = 0x0040, R3 = 0x0080, LB = 0x0100, RB = 0x0200,
                          A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,     // por posicion: abajo, derecha, izquierda, arriba
                          Guide = 0x10000,      // Xbox / PS / HOME
                          Capture = 0x20000,    // Captura (Switch), Compartir, boton de captura del GameSir
                          Touchpad = 0x40000,   // panel tactil pulsado (PlayStation)
                          Mute = 0x80000,       // silencio del DualSense
                          P1 = 0x100000, P2 = 0x200000; // levas traseras (DualSense Edge, Switch 2 GL / GR)
        public const uint Dpad = Up | Down | Left | Right;
    }

    enum PadKind { XInput, Xbox, GameSir, EightBitDo, DualShock4, DualSense, DualSenseEdge, SwitchPro, JoyConL, JoyConR, Switch2Pro, Generic }

    // Lo combinado de todos los mandos (botones juntos; de cada stick y gatillo, el que mas se mueve)
    struct PadSnapshot
    {
        public uint Buttons;
        public byte LT, RT;
        public short LX, LY, RX, RY;
        public int Count;
        public bool KbdCapture; // la captura de ahora viene del teclado del receptor del GameSir (ver GamepadLoop)
    }

    sealed class Pad
    {
        public string Source = "";      // "XInput 0", "HID USB", "HID Bluetooth", "winmm 3"
        public PadKind Kind;
        public string Name = "";
        public ushort Vid, Pid;
        public bool Bluetooth;
        public uint Buttons;
        public byte LT, RT;
        public short LX, LY, RX, RY;
        public int XIndex = -1;         // XInput
        public IntPtr Dev;              // Raw Input
        public string Path = "";
        public int JoyId = -1;          // winmm
        public bool JoyRightZR, JoyPov;
        public long Reports;
        public DateTime Since = DateTime.Now;
        // HID: parser propio (Sony / Nintendo) o el del descriptor (HidP)
        public int Parser;              // 0 descriptor, 1 DualShock 4, 2 DualSense, 3 Switch
        public IntPtr Preparsed = IntPtr.Zero;
        public uint[] BtnMap;           // uso del boton (pagina 9) -> PadBtn (o PadHub.TrigL / TrigR)
        public HidVal VX, VY, VRX, VRY, VLT, VRT, VHat;
        public bool CombinedZ;          // Z = los dos gatillos juntos
        public bool[] AxisOk;           // ejes en reposo al conectar (X, Y, RX, RY, LT, RT); los otros (pedales, acelerador) no se usan
        public double[] AxisVal = new double[6];
        public bool[] AxisGot = new bool[6];
        public byte[] Buf = new byte[0];
        public ushort[] Usages = new ushort[128];
        public DateTime HandshakeAt = DateTime.MinValue; // Mando Pro de Switch por USB: sin informes completos, se le pide (una vez)
        public bool HandshakeDone, FullReports;

        public string Describe()
        {
            return Name + " (" + Source + (Vid != 0 || Pid != 0 ? ", " + Vid.ToString("X4") + ":" + Pid.ToString("X4") : "") + ")";
        }
    }

    sealed class HidVal
    {
        public ushort Page, Usage;
        public int Min, Max;
        public bool Signed;
    }

    // ---------------- Nombres de los botones segun el mando ----------------
    static class PadNames
    {
        public static bool IsPlayStation(PadKind k) { return k == PadKind.DualShock4 || k == PadKind.DualSense || k == PadKind.DualSenseEdge; }
        public static bool IsNintendo(PadKind k) { return k == PadKind.SwitchPro || k == PadKind.JoyConL || k == PadKind.JoyConR || k == PadKind.Switch2Pro || k == PadKind.EightBitDo; }

        public static string KindName(PadKind k)
        {
            switch (k)
            {
                case PadKind.Xbox: return "Mando Xbox";
                case PadKind.GameSir: return "GameSir";
                case PadKind.EightBitDo: return "8BitDo";
                case PadKind.DualShock4: return "DualShock 4";
                case PadKind.DualSense: return "DualSense";
                case PadKind.DualSenseEdge: return "DualSense Edge";
                case PadKind.SwitchPro: return "Mando Pro de Switch";
                case PadKind.JoyConL: return "Joy-Con izquierdo";
                case PadKind.JoyConR: return "Joy-Con derecho";
                case PadKind.Switch2Pro: return "Mando Pro de Switch 2";
                case PadKind.XInput: return "Mando XInput";
                default: return "Mando";
            }
        }

        // Nombre de un boton (uno solo) en el mando de ese tipo. screen = con tildes (avisos en pantalla); sin, para el log
        public static string Name(PadKind k, uint b, bool screen)
        {
            bool ps = IsPlayStation(k), nin = IsNintendo(k);
            switch (b)
            {
                case PadBtn.A: return ps ? "Cruz" : nin ? "B" : "A";
                case PadBtn.B: return ps ? (screen ? "Círculo" : "Circulo") : nin ? "A" : "B";
                case PadBtn.X: return ps ? "Cuadrado" : nin ? "Y" : "X";
                case PadBtn.Y: return ps ? (screen ? "Triángulo" : "Triangulo") : nin ? "X" : "Y";
                case PadBtn.LB: return ps ? "L1" : nin ? "L" : "LB";
                case PadBtn.RB: return ps ? "R1" : nin ? "R" : "RB";
                case PadBtn.View: return k == PadKind.DualShock4 ? "Share" : ps ? "Crear" : nin ? "-" : "Vista";
                case PadBtn.Start: return ps ? (screen ? "Opciones" : "Options") : nin ? "+" : (screen ? "Menú" : "Menu");
                case PadBtn.L3: return "L3";
                case PadBtn.R3: return "R3";
                case PadBtn.Guide: return ps ? "PS" : nin ? "HOME" : k == PadKind.Xbox ? "Xbox" : (screen ? "Guía" : "Guia");
                case PadBtn.Capture: return nin ? "Captura" : k == PadKind.GameSir ? (screen ? "botón de captura" : "boton de captura") : "Compartir";
                case PadBtn.Touchpad: return screen ? "panel táctil" : "panel tactil";
                case PadBtn.Mute: return "silencio";
                case PadBtn.P1: return nin ? "GL" : "leva izquierda";
                case PadBtn.P2: return nin ? "GR" : "leva derecha";
                case PadBtn.Up: return "cruceta arriba";
                case PadBtn.Down: return "cruceta abajo";
                case PadBtn.Left: return "cruceta izquierda";
                case PadBtn.Right: return "cruceta derecha";
            }
            return "?";
        }

        // Los botones que tiene ese mando (para nombrarlos en los avisos)
        public static uint Has(PadKind k)
        {
            const uint basic = 0xF3FF; // cruceta, A/B/X/Y, LB/RB, Vista/Menu, L3/R3
            if (k == PadKind.JoyConL) return PadBtn.Dpad | PadBtn.View | PadBtn.L3 | PadBtn.LB | PadBtn.Capture;
            if (k == PadKind.JoyConR) return PadBtn.A | PadBtn.B | PadBtn.X | PadBtn.Y | PadBtn.Start | PadBtn.R3 | PadBtn.RB | PadBtn.Guide;
            uint m = basic | PadBtn.Guide;
            if (IsPlayStation(k)) m |= PadBtn.Touchpad | (k != PadKind.DualShock4 ? PadBtn.Mute : 0) | (k == PadKind.DualSenseEdge ? PadBtn.P1 | PadBtn.P2 : 0);
            if (IsNintendo(k) || k == PadKind.GameSir) m |= PadBtn.Capture;
            return m;
        }

        public static string TriggerName(PadKind k, bool left)
        {
            return IsPlayStation(k) ? (left ? "L2" : "R2") : IsNintendo(k) ? (left ? "ZL" : "ZR") : (left ? "LT" : "RT");
        }

        // Varios botones juntos ("Cruz + R1")
        public static string List(PadKind k, uint bits, bool screen)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                uint b = 1u << i;
                if ((bits & b) == 0) continue;
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append(Name(k, b, screen));
            }
            return sb.ToString();
        }

        // Lo que hace cada boton con la imagen de la consola delante (GamepadLoop), con los nombres de este mando (para el log)
        public static string HelpLine(PadKind k)
        {
            if (BotonesCe.Activo) return BotonesCe.Ayuda(k, false); // (ConsoleEnhancer: Options/Menu/+ = modo, Vista/Crear/- = generacion; src\BotonesCe.cs)
            string lt = TriggerName(k, true), rt = TriggerName(k, false);
            string s = "cruceta = captura, " + Name(k, PadBtn.X, false) + "/" + Name(k, PadBtn.Y, false) + "/" + Name(k, PadBtn.B, false) +
                       " = 1/2/3 pasadas de DLSS 5, " + Name(k, PadBtn.A, false) + " = DLSS 5 si/no, " +
                       Name(k, PadBtn.LB, false) + "/" + lt + "/" + rt + "/" + Name(k, PadBtn.RB, false) + " = nivel 1-4, " +
                       Name(k, PadBtn.View, false) + " / " + Name(k, PadBtn.Start, false) + " = modo 30 / 60 / 120 fps (baja / sube; 120 solo si la captura y la tele lo admiten), " +
                       "L3 = tipo de generacion (selector; mantenido 1 s = filtro de tramado), R3 = escalador";
            if (IsNintendo(k) || k == PadKind.GameSir) s += ", " + Name(k, PadBtn.Capture, false) + " = generacion de fotogramas";
            else if (IsPlayStation(k)) s += ", panel tactil = generacion de fotogramas";
            return s;
        }

        // Lo del modo PC (PcMode), con los nombres de este mando
        public static string PcHelpLine(PadKind k)
        {
            string g = Name(k, PadBtn.Guide, false);
            return "stick izquierdo = raton, stick derecho = rueda, " + Name(k, PadBtn.A, false) + " = clic izquierdo, " +
                   Name(k, PadBtn.X, false) + " = clic derecho, R3 = clic central, " + Name(k, PadBtn.B, false) + " = Esc, cruceta = flechas, " +
                   Name(k, PadBtn.View, false) + " = Alt, " + Name(k, PadBtn.Start, false) + " = F4 (las dos: Alt+F4), L3 = teclado en pantalla" +
                   (PadOptions.GuideApp.Length > 0 ? ", " + g + " = " + System.IO.Path.GetFileName(PadOptions.GuideApp) : "") +
                   "; mantener " + g + " o " + Name(k, PadBtn.LB, false) + "+" + Name(k, PadBtn.RB, false) + " 1 s = encenderlo / apagarlo";
        }
    }

    // ---------------- Claves del ini (mando_*) ----------------
    static class PadOptions
    {
        public static volatile bool PcMode = true;        // mando_pc
        public static string GuideApp = "";               // mando_pc_guia
        public static double Speed = 1.0;                 // mando_pc_velocidad
        public static string[] FullscreenOk = new string[0]; // mando_pc_pantalla_completa
        public static volatile bool BlockJoyxoff = true;  // mando_bloquear_joyxoff: con la imagen delante, tragarse la entrada inyectada que no sea de la app (Joyxoff)
        // 19-11: mando_winmm (0 de serie): joyGetPosEx / joyGetDevCaps de winmm, el ultimo recurso para mandos que no ven ni XInput ni Raw
        // Input. Nunca ha encontrado ninguno en este PC y, tras conectar o quitar un dispositivo, hacia que dinput.dll de Windows (DIHid_CheckHidList,
        // un error suyo de un elemento de mas al compactar su lista) pisara el heap del proceso: la app se caia (0xc0000374) a los segundos o al
        // pedir memoria otro hilo (volcados de 18-11 19:21 y 19-11 02:40, analizados con dbgeng)
        public static volatile bool Winmm = false;

        static bool Flag(string val) { string v = val.Trim().ToLowerInvariant(); return v == "1" || v == "true" || v == "si" || v == "yes" || v == "on"; }

        public static bool Parse(string key, string val)
        {
            switch (key)
            {
                case "mando_bloquear_joyxoff": BlockJoyxoff = Flag(val); return true;
                case "mando_winmm": Winmm = Flag(val); return true;
                case "mando_pc": { string v = val.Trim().ToLowerInvariant(); PcMode = v == "1" || v == "true" || v == "si" || v == "yes" || v == "on"; return true; }
                case "mando_pc_guia": GuideApp = val.Trim(); return true;
                case "mando_pc_velocidad":
                    {
                        double d;
                        if (double.TryParse(val.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) Speed = Math.Max(0.1, Math.Min(5, d));
                        return true;
                    }
                case "mando_pc_pantalla_completa":
                    {
                        List<string> l = new List<string>();
                        foreach (string p in val.Split(',', ';'))
                            if (p.Trim().Length > 0) l.Add(p.Trim());
                        FullscreenOk = l.ToArray();
                        return true;
                    }
            }
            return false;
        }

        public static string Describe()
        {
            return "mando_pc=" + (PcMode ? "1" : "0") + ", mando_pc_guia=" + (GuideApp.Length > 0 ? GuideApp : "(nada)") +
                   ", mando_pc_velocidad=" + Speed.ToString("0.##", CultureInfo.InvariantCulture) +
                   ", mando_pc_pantalla_completa=" + (FullscreenOk.Length > 0 ? string.Join(", ", FullscreenOk) : "(ninguno)") +
                   ", mando_bloquear_joyxoff=" + (BlockJoyxoff ? "1" : "0");
        }
    }

    // ---------------- Capa de entrada ----------------
    sealed class PadHub
    {
        public static readonly PadHub Shared = new PadHub();
        public static volatile Action<string> LogFn; // (null = Log.Write)
        public const uint TrigL = 0x80000000, TrigR = 0x40000000; // en BtnMap: gatillo digital

        readonly object sync = new object();      // listas y estado de los mandos
        readonly object xsync = new object();     // llamadas a XInput (dos hilos leen)
        readonly Pad[] xpads = new Pad[4];
        readonly Dictionary<IntPtr, Pad> hidPads = new Dictionary<IntPtr, Pad>();
        readonly List<Pad> joyPads = new List<Pad>();
        readonly Dictionary<IntPtr, bool> kbds = new Dictionary<IntPtr, bool>(); // teclados vistos: true = el del receptor GameSir
        readonly HashSet<uint> hidSeen = new HashSet<uint>();  // VID:PID de todo lo HID visto (tambien lo que lee XInput), para winmm
        bool started, rawOk, xinputOk = true, getStateExOk = true, capsExOk = true;
        volatile bool xRescan = true, joyRescan = true;
        DateTime lastXScan = DateTime.MinValue, joyRescanAt = DateTime.MinValue;
        volatile bool kbdCapture;
        DateTime kbdCaptureAt = DateTime.MinValue;
        int connects;
        PadKind mainKind = PadKind.XInput;
        IntPtr hwnd;
        WndProcFn wndProc;
        bool timerOn;

        public int Connects { get { return connects; } }
        public bool RawInputOk { get { return rawOk; } }
        // 21-11 (1.2.1): la ultima entrada de teclado / raton que ha visto Raw Input (Environment.TickCount; 0 = ninguna aun), fisica o
        // inyectada: los ganchos de bajo nivel ven las dos, el mando (XInput) ninguna. Para el vigilante de los ganchos (MenuJuego.cs
        // GanchosVigila): GetLastInputInfo tambien cuenta el mando XInput y los volvia a poner cada 2 s jugando con el
        volatile int lastKbdTick, lastMouseTick;
        volatile bool rawMouseOk;
        public int LastKbdTick { get { return lastKbdTick; } }
        public int LastMouseTick { get { return lastMouseTick; } }
        public bool RawMouseOk { get { return rawOk && rawMouseOk; } }
        public PadKind MainKind { get { lock (sync) return mainKind; } }

        static void Say(string s)
        {
            Action<string> f = LogFn;
            if (f != null) f(s); else Log.Write(s);
        }

        public void Start()
        {
            lock (sync)
            {
                if (started) return;
                started = true;
            }
            Thread t = new Thread(RawThread);
            t.IsBackground = true;
            t.Name = "Mandos (Raw Input)";
            t.Start();
            // 22-11 (1.2.2, MENU): la Guia mirada cada 10 ms (ver GuidePresses). (REVISION: la arranca la primera lectura del contador -la
            // app, GamepadLoop-; no aqui: la espera y el residente del modo PC tambien arrancan PadHub y no la usan, y con un mando XInput
            // conectado, el receptor del GameSir por ejemplo, la espera despertaba ~64-100 veces por segundo toda la noche por ella)
        }

        // ---- 22-11 (1.2.2, MENU; el usuario: "a veces le doy al boton del home del mando de pc y no se abre el menu de config") ----
        // Cada pulsacion de la Guia (Xbox / PS / HOME) se cuenta al verla por primera vez, la vea quien la vea: cualquier lectura de XInput
        // (GamepadLoop cada ~50 ms, el modo PC cada 8-50 ms y esta vigilancia cada 10 ms) o un informe HID (DualSense y demas, al llegar).
        // Antes GamepadLoop solo comparaba su lectura con la anterior: una pulsacion corta entre dos lecturas suyas (<50-60 ms) se perdia.
        // El menu de ajustes (MenuAjustes.cs / AjustesGuia.cs) mira este contador: ninguna pulsacion se pierde
        int guidePresses;
        DateTime guideLastPress = DateTime.MinValue;
        int guideWatchOn;
        public int GuidePresses
        {
            get
            {
                if (started && Interlocked.Exchange(ref guideWatchOn, 1) == 0)
                {
                    Thread g = new Thread(GuideWatch);
                    g.IsBackground = true;
                    g.Name = "Mandos (Guia)";
                    g.Start();
                }
                return Thread.VolatileRead(ref guidePresses);
            }
        }
        public DateTime GuideLastPress { get { lock (sync) return guideLastPress; } }

        // (con sync) el boton de un mando antes y despues de leerlo: si la Guia acaba de bajar, una pulsacion mas
        void GuideEdge(uint before, uint after)
        {
            if ((before & PadBtn.Guide) != 0 || (after & PadBtn.Guide) == 0) return;
            Interlocked.Increment(ref guidePresses);
            guideLastPress = DateTime.Now;
        }

        // Solo XInput (lo HID llega solo): con algun mando XInput conectado, una lectura cada 10 ms (~40 us cada una: <0,5 % de un nucleo).
        // Sin ninguno no lee nada (el sondeo de los huecos vacios sigue siendo el de GamepadLoop, cada 3 s)
        void GuideWatch()
        {
            try
            {
                while (true)
                {
                    Thread.Sleep(10);
                    if (!xinputOk) return;
                    bool any = false;
                    lock (sync) for (int i = 0; i < 4; i++) if (xpads[i] != null) any = true;
                    if (!any) { Thread.Sleep(240); continue; }
                    PollXInput();
                }
            }
            catch (Exception ex) { Say("Mandos (Guia): " + ex.Message); }
        }

        // ---- Lectura (GamepadLoop cada 50 ms; PcMode cada 50 ms, o 8 ms con un stick movido) ----
        public void Poll(out PadSnapshot s)
        {
            PollXInput();
            DateTime now = DateTime.UtcNow;
            if (PadOptions.Winmm && joyRescan && (now - joyRescanAt).TotalSeconds >= 2) ScanWinmm(); // (19-11: apagado de serie, ver PadOptions.Winmm)
            lock (sync)
            {
                foreach (Pad p in joyPads) { uint b0 = p.Buttons; ReadWinmm(p); GuideEdge(b0, p.Buttons); }
                s = new PadSnapshot();
                for (int i = 0; i < 4; i++) if (xpads[i] != null) Merge(ref s, xpads[i]);
                foreach (Pad p in hidPads.Values) Merge(ref s, p);
                foreach (Pad p in joyPads) Merge(ref s, p);
                if (KbdCaptureNow()) { s.Buttons |= PadBtn.Capture; s.KbdCapture = true; }
            }
        }

        // El receptor suelta la Win enseguida: pulsado al menos 120 ms (lo ven dos lecturas de 50 ms); sin soltar, se suelta a los 2 s
        bool KbdCaptureNow()
        {
            double ms = (DateTime.UtcNow - kbdCaptureAt).TotalMilliseconds;
            if (kbdCapture && ms > 2000) kbdCapture = false;
            return kbdCapture || ms < 120;
        }

        static void Merge(ref PadSnapshot s, Pad p)
        {
            s.Count++;
            s.Buttons |= p.Buttons;
            if (p.LT > s.LT) s.LT = p.LT;
            if (p.RT > s.RT) s.RT = p.RT;
            if (Math.Abs((int)p.LX) > Math.Abs((int)s.LX)) s.LX = p.LX;
            if (Math.Abs((int)p.LY) > Math.Abs((int)s.LY)) s.LY = p.LY;
            if (Math.Abs((int)p.RX) > Math.Abs((int)s.RX)) s.RX = p.RX;
            if (Math.Abs((int)p.RY) > Math.Abs((int)s.RY)) s.RY = p.RY;
        }

        // Copia de los mandos de ahora (pruebas y nombres)
        public List<Pad> Pads()
        {
            List<Pad> l = new List<Pad>();
            lock (sync)
            {
                for (int i = 0; i < 4; i++) if (xpads[i] != null) l.Add(Copy(xpads[i]));
                foreach (Pad p in hidPads.Values) l.Add(Copy(p));
                foreach (Pad p in joyPads) l.Add(Copy(p));
                if (KbdCaptureNow())
                {
                    bool put = false;
                    foreach (Pad p in l) if (p.Vid == 0x3537) { p.Buttons |= PadBtn.Capture; put = true; }
                    if (!put && l.Count > 0) l[0].Buttons |= PadBtn.Capture;
                }
            }
            return l;
        }

        static Pad Copy(Pad p)
        {
            Pad c = new Pad();
            c.Source = p.Source; c.Kind = p.Kind; c.Name = p.Name; c.Vid = p.Vid; c.Pid = p.Pid; c.Bluetooth = p.Bluetooth;
            c.Buttons = p.Buttons; c.LT = p.LT; c.RT = p.RT; c.LX = p.LX; c.LY = p.LY; c.RX = p.RX; c.RY = p.RY;
            c.XIndex = p.XIndex; c.Dev = p.Dev; c.Path = p.Path; c.JoyId = p.JoyId; c.Reports = p.Reports; c.Since = p.Since;
            c.Parser = p.Parser;
            return c;
        }

        void Connected(Pad p)
        {
            connects++;
            if (Count() == 1) mainKind = p.Kind;
            Say("Mando conectado: " + p.Describe() + ": con la imagen de la consola delante, " + PadNames.HelpLine(p.Kind));
        }

        void Disconnected(Pad p)
        {
            Say("Mando desconectado: " + p.Describe());
            if (Count() > 0)
            {
                for (int i = 0; i < 4; i++) if (xpads[i] != null) { mainKind = xpads[i].Kind; return; }
                foreach (Pad q in hidPads.Values) { mainKind = q.Kind; return; }
                foreach (Pad q in joyPads) { mainKind = q.Kind; return; }
            }
        }

        int Count()
        {
            int n = hidPads.Count + joyPads.Count;
            for (int i = 0; i < 4; i++) if (xpads[i] != null) n++;
            return n;
        }

        // ================= XInput =================
        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; public uint Spare; }
        [StructLayout(LayoutKind.Sequential)] struct XVib { public ushort L, R; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint XGetState(uint i, out XState s);
        [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern uint XGetStateEx(uint i, out XState s); // (con el boton Guia: 0x0400)
        [DllImport("xinput1_4.dll", EntryPoint = "#108")] static extern uint XGetCapsEx(uint one, uint i, uint flags, byte[] caps);
        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")] static extern uint XSetState(uint i, ref XVib v);

        uint XRead(uint i, out XState st)
        {
            if (getStateExOk)
            {
                try { return XGetStateEx(i, out st); }
                catch (EntryPointNotFoundException) { getStateExOk = false; }
            }
            return XGetState(i, out st);
        }

        void PollXInput()
        {
            if (!xinputOk) return;
            lock (xsync)
            {
                try
                {
                    DateTime now = DateTime.UtcNow;
                    // Un hueco vacio cuesta (XInput busca el mando): cada 3 s, o enseguida si Windows avisa de un mando nuevo
                    bool scan = xRescan || (now - lastXScan).TotalSeconds >= 3;
                    if (scan) { lastXScan = now; xRescan = false; }
                    for (uint i = 0; i < 4; i++)
                    {
                        Pad p;
                        lock (sync) p = xpads[i];
                        if (p == null && !scan) continue;
                        XState st;
                        uint r = XRead(i, out st);
                        if (r != 0)
                        {
                            if (p != null) lock (sync) { xpads[i] = null; Disconnected(p); }
                            continue;
                        }
                        if (p == null)
                        {
                            p = NewXPad((int)i);
                            lock (sync) { xpads[i] = p; Connected(p); }
                        }
                        uint b = st.Buttons;
                        uint u = b & 0xF3FF;                       // (0x0400 = Guia con GetStateEx; 0x0800 sin uso)
                        if ((b & 0x0400) != 0) u |= PadBtn.Guide;
                        lock (sync)
                        {
                            GuideEdge(p.Buttons, u); // (22-11, 1.2.2: la Guia contada al verla, ver GuidePresses)
                            p.Buttons = u; p.LT = st.LT; p.RT = st.RT; p.LX = st.LX; p.LY = st.LY; p.RX = st.RX; p.RY = st.RY;
                            p.Reports = st.Packet;
                        }
                    }
                }
                catch (DllNotFoundException) { xinputOk = false; Say("Sin XInput (xinput1_4.dll): solo los mandos HID"); }
            }
        }

        Pad NewXPad(int i)
        {
            Pad p = new Pad();
            p.Source = "XInput " + i;
            p.XIndex = i;
            if (capsExOk)
            {
                byte[] c = new byte[64];
                try
                {
                    if (XGetCapsEx(1, (uint)i, 0, c) == 0)
                    {
                        p.Vid = BitConverter.ToUInt16(c, 20);
                        p.Pid = BitConverter.ToUInt16(c, 22);
                        if ((BitConverter.ToUInt16(c, 2) & 0x0002) != 0) p.Source += ", inalambrico";
                    }
                }
                catch (EntryPointNotFoundException) { capsExOk = false; }
            }
            p.Kind = p.Vid == 0x045E ? PadKind.Xbox : p.Vid == 0x3537 ? PadKind.GameSir : p.Vid == 0x2DC8 ? PadKind.EightBitDo : PadKind.XInput;
            if (p.Kind == PadKind.EightBitDo) p.Kind = PadKind.XInput; // (en X-input los botones van como los de Xbox)
            string prod = p.Vid != 0 ? ProductFor(p.Vid, p.Pid) : null;
            if (p.Vid == 0x045E && p.Pid == 0x028E) p.Name = "Mando Xbox 360 (o uno que lo imita)";
            else if (prod != null && p.Kind != PadKind.Xbox) p.Name = prod;
            else p.Name = p.Kind == PadKind.Xbox ? (prod != null && prod.IndexOf("xbox", StringComparison.OrdinalIgnoreCase) >= 0 ? prod : "Mando Xbox") :
                          p.Vid == 0x2DC8 ? "8BitDo (modo X-input)" : PadNames.KindName(p.Kind);
            if (p.Kind == PadKind.GameSir && p.Name.IndexOf("gamesir", StringComparison.OrdinalIgnoreCase) < 0) p.Name = "GameSir " + p.Name;
            return p;
        }

        // Un pulso (o dos) de vibracion en los mandos XInput (el modo PC al encenderse / apagarse, como Joyxoff)
        public void Rumble(int pulses)
        {
            if (!xinputOk) return;
            List<uint> idx = new List<uint>();
            lock (sync) for (uint i = 0; i < 4; i++) if (xpads[i] != null) idx.Add(i);
            if (idx.Count == 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    for (int k = 0; k < pulses; k++)
                    {
                        XVib on = new XVib(); on.L = 30000; on.R = 30000;
                        XVib off = new XVib();
                        foreach (uint i in idx) XSetState(i, ref on);
                        Thread.Sleep(110);
                        foreach (uint i in idx) XSetState(i, ref off);
                        Thread.Sleep(110);
                    }
                }
                catch { }
            });
        }

        // Coste de leer: un mando conectado y un hueco vacio (para --test-mandos)
        public string MeasureXInput()
        {
            if (!xinputOk) return "XInput: no disponible";
            StringBuilder sb = new StringBuilder("XInput (xinput1_4" + (getStateExOk ? ", con XInputGetStateEx para la Guia" : "") + "): ");
            lock (xsync)
            {
                for (uint i = 0; i < 4; i++)
                {
                    XState st;
                    Stopwatch w = Stopwatch.StartNew();
                    uint r = 0;
                    int n = 20;
                    for (int k = 0; k < n; k++) r = XRead(i, out st);
                    double us = w.Elapsed.TotalMilliseconds * 1000 / n;
                    sb.Append("hueco ").Append(i).Append(r == 0 ? " con mando " : " vacio ").Append(us.ToString("0", CultureInfo.InvariantCulture)).Append(" us; ");
                }
            }
            return sb.ToString();
        }

        // ================= winmm (ultimo recurso) =================
        [StructLayout(LayoutKind.Sequential)]
        struct JOYINFOEX { public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, POV, Res1, Res2; }
        [DllImport("winmm.dll")] static extern uint joyGetNumDevs();
        [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW")] static extern uint joyGetDevCaps(UIntPtr id, byte[] caps, uint size);
        [DllImport("winmm.dll")] static extern uint joyGetPosEx(uint id, ref JOYINFOEX ji);

        // Solo al empezar y cuando Windows avisa de un cambio (2 s despues): con los huecos vacios joyGetPosEx tarda
        void ScanWinmm()
        {
            joyRescan = false;
            joyRescanAt = DateTime.UtcNow;
            try
            {
                uint n = Math.Min(16, joyGetNumDevs());
                List<int> found = new List<int>();
                for (uint id = 0; id < n; id++)
                {
                    JOYINFOEX ji = new JOYINFOEX();
                    ji.Size = (uint)Marshal.SizeOf(typeof(JOYINFOEX));
                    ji.Flags = 0xFF;
                    if (joyGetPosEx(id, ref ji) != 0) continue;
                    byte[] caps = new byte[728];
                    if (joyGetDevCaps((UIntPtr)id, caps, 728) != 0) continue;
                    ushort vid = BitConverter.ToUInt16(caps, 0), pid = BitConverter.ToUInt16(caps, 2);
                    lock (sync)
                    {
                        // Lo que ya lee XInput o Raw Input no se repite (por VID:PID); sin VID:PID de XInput no se sabe: nada
                        bool covered = hidSeen.Contains(((uint)vid << 16) | pid);
                        for (int i = 0; i < 4; i++)
                            if (xpads[i] != null && (xpads[i].Vid == 0 || (xpads[i].Vid == vid && xpads[i].Pid == pid))) covered = true;
                        foreach (Pad h in hidPads.Values) if (h.Vid == vid && h.Pid == pid) covered = true;
                        if (covered) continue;
                        found.Add((int)id);
                        bool have = false;
                        foreach (Pad j in joyPads) if (j.JoyId == (int)id) have = true;
                        if (have) continue;
                        Pad p = new Pad();
                        p.Source = "winmm " + id;
                        p.JoyId = (int)id;
                        p.Vid = vid; p.Pid = pid;
                        string nm = Encoding.Unicode.GetString(caps, 4, 64);
                        int z = nm.IndexOf('\0');
                        if (z >= 0) nm = nm.Substring(0, z);
                        p.Kind = vid == 0x2DC8 ? PadKind.EightBitDo : vid == 0x3537 ? PadKind.GameSir : PadKind.Generic;
                        p.Name = nm.Trim().Length > 0 ? nm.Trim() : "Mando generico";
                        uint wcaps = BitConverter.ToUInt32(caps, 128), nbtn = BitConverter.ToUInt32(caps, 92);
                        p.JoyRightZR = (wcaps & 0x3) == 0x3;    // Z y R: el stick derecho (si no, U y V)
                        p.JoyPov = (wcaps & 0x10) != 0;
                        p.BtnMap = GenericMap(p.Kind, (int)nbtn);
                        joyPads.Add(p);
                        Connected(p);
                    }
                }
                lock (sync)
                {
                    for (int k = joyPads.Count - 1; k >= 0; k--)
                        if (!found.Contains(joyPads[k].JoyId)) { Pad p = joyPads[k]; joyPads.RemoveAt(k); Disconnected(p); }
                }
            }
            catch (DllNotFoundException) { }
            catch (Exception ex) { Say("Aviso: mandos por winmm: " + ex.Message); }
        }

        void ReadWinmm(Pad p)
        {
            JOYINFOEX ji = new JOYINFOEX();
            ji.Size = (uint)Marshal.SizeOf(typeof(JOYINFOEX));
            ji.Flags = 0xFF;
            if (joyGetPosEx((uint)p.JoyId, ref ji) != 0) { joyRescan = true; return; }
            uint b = 0;
            int lt = 0, rt = 0;
            for (int i = 0; i < 32; i++)
            {
                if ((ji.Buttons & (1u << i)) == 0) continue;
                uint m = i + 1 < p.BtnMap.Length ? p.BtnMap[i + 1] : 0;
                if (m == TrigL) lt = 255; else if (m == TrigR) rt = 255; else b |= m;
            }
            if (p.JoyPov && ji.POV != 0xFFFF && ji.POV <= 36000) b |= HatBits((int)((ji.POV + 2250) % 36000 / 4500));
            p.Buttons = b;
            p.LX = Axis16(ji.X, false); p.LY = Axis16(ji.Y, true);
            p.RX = Axis16(p.JoyRightZR ? ji.Z : ji.U, false); p.RY = Axis16(p.JoyRightZR ? ji.R : ji.V, true);
            p.LT = (byte)lt; p.RT = (byte)rt;
            p.Reports++;
        }

        static short Axis16(uint v, bool invert)
        {
            int s = (int)Math.Min(65535u, v) - 32768;
            if (invert) s = -s - 1;
            return (short)Math.Max(-32768, Math.Min(32767, s));
        }

        // ================= Raw Input (HID y el teclado del receptor GameSir) =================
        delegate IntPtr WndProcFn(IntPtr h, uint m, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public uint cbSize, style; public WndProcFn lpfnWndProc; public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
        }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICELIST { public IntPtr Device; public uint Type; }
        [StructLayout(LayoutKind.Sequential)] struct DEV_BROADCAST_DEVICEINTERFACE { public int Size, DeviceType, Reserved; public Guid ClassGuid; public short Name; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr fn);
        [DllImport("user32.dll")] static extern bool KillTimer(IntPtr h, IntPtr id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devs, uint n, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr raw, uint cmd, IntPtr data, ref uint size, uint header);
        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")] static extern uint GetRawInputDeviceInfo(IntPtr dev, uint cmd, IntPtr data, ref uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] list, ref uint n, uint size);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr RegisterDeviceNotification(IntPtr to, ref DEV_BROADCAST_DEVICEINTERFACE f, uint flags);

        [DllImport("hid.dll")] static extern bool HidD_GetProductString(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, byte[] caps);
        [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, byte[] caps, ref ushort len, IntPtr pp);
        [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, byte[] caps, ref ushort len, IntPtr pp);
        [DllImport("hid.dll")] static extern int HidP_GetUsages(int type, ushort page, ushort link, ushort[] list, ref uint len, IntPtr pp, byte[] rep, uint repLen);
        [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint v, IntPtr pp, byte[] rep, uint repLen);
        [DllImport("hid.dll")] static extern int HidP_GetScaledUsageValue(int type, ushort page, ushort link, ushort usage, out int v, IntPtr pp, byte[] rep, uint repLen);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr h, byte[] buf, uint len, out uint done, IntPtr ov);

        const uint WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_DEVICECHANGE = 0x0219, WM_TIMER = 0x0113;
        const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007, RIDI_DEVICEINFO = 0x2000000b, RIDI_PREPARSEDDATA = 0x20000005;
        const int HIDP_OK = 0x00110000;
        static readonly Guid XusbGuid = new Guid("EC87F1E3-C13B-4100-B5F7-8B84D54260CB");
        static readonly Guid HidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
        IntPtr rawBuf = IntPtr.Zero;
        uint rawBufLen;

        void RawThread()
        {
            try
            {
                wndProc = WndProc;
                WNDCLASSEX wc = new WNDCLASSEX();
                wc.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
                wc.lpfnWndProc = wndProc;
                wc.hInstance = GetModuleHandle(null);
                wc.lpszClassName = "ConsoleEnhancer_Mandos";
                RegisterClassEx(ref wc);
                hwnd = CreateWindowEx(0, wc.lpszClassName, "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero); // HWND_MESSAGE
                if (hwnd == IntPtr.Zero) { Say("Aviso: mandos HID: no se pudo crear la ventana de mensajes (" + Marshal.GetLastWin32Error() + ")"); return; }
                // Joystick (4), gamepad (5), multieje (8) y teclado (6, por el boton de captura del GameSir), tambien sin el foco
                // (INPUTSINK) y con aviso de conectar / quitar (DEVNOTIFY: al registrarse llegan los que ya estan)
                RAWINPUTDEVICE[] r = new RAWINPUTDEVICE[4];
                ushort[] us = { 4, 5, 8, 6 };
                for (int i = 0; i < 4; i++) { r[i].UsagePage = 1; r[i].Usage = us[i]; r[i].Flags = 0x00000100 | 0x00002000; r[i].Target = hwnd; }
                rawOk = RegisterRawInputDevices(r, 4, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
                if (!rawOk) Say("Aviso: mandos HID: Raw Input no disponible (" + Marshal.GetLastWin32Error() + "): solo XInput y winmm");
                // 21-11 (1.2.1): y el raton (1, 2), tambien sin el foco y sin avisos de conectar: solo la hora de su ultima entrada (LastMouseTick)
                if (rawOk)
                {
                    RAWINPUTDEVICE[] rm = new RAWINPUTDEVICE[1];
                    rm[0].UsagePage = 1; rm[0].Usage = 2; rm[0].Flags = 0x00000100; rm[0].Target = hwnd;
                    rawMouseOk = RegisterRawInputDevices(rm, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
                }
                // Mandos XInput nuevos (la interfaz XUSB) y cualquier HID (para winmm): se buscan al momento
                foreach (Guid g in new Guid[] { XusbGuid, HidGuid })
                {
                    DEV_BROADCAST_DEVICEINTERFACE f = new DEV_BROADCAST_DEVICEINTERFACE();
                    f.Size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE));
                    f.DeviceType = 5; // DBT_DEVTYP_DEVICEINTERFACE
                    f.ClassGuid = g;
                    RegisterDeviceNotification(hwnd, ref f, 0);
                }
                MSG m;
                while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref m);
                    DispatchMessage(ref m);
                }
            }
            catch (Exception ex) { Log.Error("Mandos (Raw Input)", ex); }
        }

        IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            try
            {
                if (msg == WM_INPUT) OnInput(l);
                else if (msg == WM_INPUT_DEVICE_CHANGE)
                {
                    if (w.ToInt64() == 1) Arrival(l); else Removal(l);
                    xRescan = true;
                    joyRescan = true;
                }
                else if (msg == WM_DEVICECHANGE)
                {
                    long e = w.ToInt64();
                    if (e == 0x8000 || e == 0x8004) { xRescan = true; joyRescan = true; joyRescanAt = DateTime.UtcNow; } // (llegada / quitado)
                }
                else if (msg == WM_TIMER) OnTimer();
            }
            catch (Exception ex) { Log.Error("Mandos (mensaje " + msg + ")", ex); }
            return DefWindowProc(h, msg, w, l);
        }

        static string DeviceName(IntPtr dev)
        {
            uint n = 0;
            GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, IntPtr.Zero, ref n);
            if (n == 0 || n > 4096) return "";
            IntPtr b = Marshal.AllocHGlobal((int)n * 2 + 2);
            try
            {
                uint got = GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, b, ref n);
                return got == uint.MaxValue ? "" : Marshal.PtrToStringUni(b) ?? "";
            }
            finally { Marshal.FreeHGlobal(b); }
        }

        // (tipo, VID, PID, pagina, uso) de un dispositivo de Raw Input
        static bool DeviceInfo(IntPtr dev, out uint type, out ushort vid, out ushort pid, out ushort page, out ushort usage)
        {
            type = 0; vid = pid = page = usage = 0;
            uint n = 32;
            IntPtr b = Marshal.AllocHGlobal(32);
            try
            {
                Marshal.WriteInt32(b, 32);
                if (GetRawInputDeviceInfo(dev, RIDI_DEVICEINFO, b, ref n) == uint.MaxValue) return false;
                type = (uint)Marshal.ReadInt32(b, 4);
                if (type == 2)
                {
                    vid = (ushort)Marshal.ReadInt32(b, 8);
                    pid = (ushort)Marshal.ReadInt32(b, 12);
                    page = (ushort)Marshal.ReadInt16(b, 20);
                    usage = (ushort)Marshal.ReadInt16(b, 22);
                }
                return true;
            }
            finally { Marshal.FreeHGlobal(b); }
        }

        static void VidPidFromName(string name, out ushort vid, out ushort pid)
        {
            vid = pid = 0;
            string u = name.ToUpperInvariant();
            int v = u.IndexOf("VID_"), p = u.IndexOf("PID_");
            if (v < 0) { v = u.IndexOf("VID&"); if (v >= 0) v += 4; else v = -1; } else v += 4; // (Bluetooth: VID&0002054C)
            if (v >= 0 && v + 4 <= u.Length)
            {
                string s = u.Substring(v, Math.Min(8, u.Length - v));
                if (s.Length == 8 && u.Substring(v - 4, 4) == "VID&") s = s.Substring(4);
                ushort.TryParse(s.Substring(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vid);
            }
            if (p < 0) p = u.IndexOf("PID&");
            if (p >= 0 && p + 8 <= u.Length) ushort.TryParse(u.Substring(p + 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pid);
        }

        static string ProductString(string path)
        {
            if (path.Length == 0) return null;
            IntPtr f = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); // (sin acceso: solo atributos y textos)
            if (f == IntPtr.Zero || f == new IntPtr(-1)) return null;
            try
            {
                byte[] b = new byte[512];
                if (!HidD_GetProductString(f, b, b.Length)) return null;
                string s = Encoding.Unicode.GetString(b);
                int z = s.IndexOf('\0');
                if (z >= 0) s = s.Substring(0, z);
                s = s.Trim();
                return s.Length > 0 ? s : null;
            }
            finally { CloseHandle(f); }
        }

        // Nombre de producto de un mando XInput: de cualquier interfaz HID suya (el GameSir tiene la de teclado)
        static string ProductFor(ushort vid, ushort pid)
        {
            try
            {
                string key = "VID_" + vid.ToString("X4") + "&PID_" + pid.ToString("X4");
                foreach (RAWINPUTDEVICELIST d in DeviceList())
                {
                    string n = DeviceName(d.Device);
                    if (n.ToUpperInvariant().IndexOf(key) < 0) continue;
                    string s = ProductString(n);
                    if (s != null) return s;
                }
            }
            catch { }
            return null;
        }

        static RAWINPUTDEVICELIST[] DeviceList()
        {
            uint n = 0, sz = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
            GetRawInputDeviceList(null, ref n, sz);
            if (n == 0) return new RAWINPUTDEVICELIST[0];
            RAWINPUTDEVICELIST[] l = new RAWINPUTDEVICELIST[n + 8];
            uint m = n + 8;
            uint got = GetRawInputDeviceList(l, ref m, sz);
            if (got == uint.MaxValue) return new RAWINPUTDEVICELIST[0];
            Array.Resize(ref l, (int)got);
            return l;
        }

        void Arrival(IntPtr dev)
        {
            uint type; ushort vid, pid, page, usage;
            if (!DeviceInfo(dev, out type, out vid, out pid, out page, out usage)) return;
            string name = DeviceName(dev);
            if (type == 1) // teclado
            {
                bool gs = name.ToUpperInvariant().IndexOf("VID_3537") >= 0;
                lock (sync) kbds[dev] = gs;
                if (gs) Say("Mandos: teclado del receptor GameSir visto (" + name + "): su boton de captura tambien llega por Raw Input");
                return;
            }
            if (type != 2) return;
            lock (sync)
            {
                hidSeen.Add(((uint)vid << 16) | pid);
                if (hidPads.ContainsKey(dev)) return;
            }
            if (page != 1 || (usage != 4 && usage != 5 && usage != 8)) return;
            // Los mandos XInput por HID (Xbox por Bluetooth, "IG_" en la ruta) ya los lee XInput
            if (name.ToUpperInvariant().IndexOf("IG_") >= 0) return;
            Pad p = new Pad();
            p.Dev = dev;
            p.Path = name;
            p.Vid = vid; p.Pid = pid;
            string un = name.ToUpperInvariant();
            p.Bluetooth = un.IndexOf("00001124-0000-1000-8000-00805F9B34FB") >= 0 || un.IndexOf("00001812-0000-1000-8000-00805F9B34FB") >= 0 || un.IndexOf("BTH") >= 0;
            p.Source = p.Bluetooth ? "HID Bluetooth" : "HID USB";
            string prod = ProductString(name);
            if (vid == 0x054C)
            {
                if (pid == 0x0CE6) { p.Kind = PadKind.DualSense; p.Parser = 2; }
                else if (pid == 0x0DF2) { p.Kind = PadKind.DualSenseEdge; p.Parser = 2; }
                else if (pid == 0x05C4 || pid == 0x09CC || pid == 0x0BA0) { p.Kind = PadKind.DualShock4; p.Parser = 1; }
            }
            else if (vid == 0x057E)
            {
                p.Parser = 3;
                p.Kind = pid == 0x2006 ? PadKind.JoyConL : pid == 0x2007 ? PadKind.JoyConR :
                         (pid == 0x2069 || pid == 0x2066 || pid == 0x2067 || pid == 0x2073) ? PadKind.Switch2Pro : PadKind.SwitchPro;
                if (!p.Bluetooth && (p.Kind == PadKind.SwitchPro || p.Kind == PadKind.Switch2Pro)) p.HandshakeAt = DateTime.UtcNow.AddSeconds(1.5);
            }
            else if (vid == 0x2DC8) p.Kind = PadKind.EightBitDo;
            else if (vid == 0x3537) p.Kind = PadKind.GameSir;
            else p.Kind = PadKind.Generic;
            if (p.Parser == 0 || p.Kind == PadKind.Switch2Pro) SetupDescriptor(p); // (el Switch 2 Pro: con el descriptor si no es como el de Switch)
            if (PadNames.IsPlayStation(p.Kind) || p.Kind == PadKind.SwitchPro || p.Kind == PadKind.JoyConL || p.Kind == PadKind.JoyConR || p.Kind == PadKind.Switch2Pro)
                p.Name = PadNames.KindName(p.Kind);
            else if (prod != null) p.Name = p.Kind == PadKind.EightBitDo && prod.IndexOf("8bitdo", StringComparison.OrdinalIgnoreCase) < 0 ? "8BitDo " + prod :
                                            p.Kind == PadKind.GameSir && prod.IndexOf("gamesir", StringComparison.OrdinalIgnoreCase) < 0 ? "GameSir " + prod : prod;
            else p.Name = PadNames.KindName(p.Kind) + (p.Kind == PadKind.Generic ? " HID" : "");
            if (p.Kind == PadKind.EightBitDo || p.Kind == PadKind.GameSir) p.Name += " (modo D-input)";
            lock (sync)
            {
                hidPads[dev] = p;
                Connected(p);
                if (p.HandshakeAt != DateTime.MinValue) EnsureTimer();
            }
        }

        void Removal(IntPtr dev)
        {
            lock (sync)
            {
                kbds.Remove(dev);
                Pad p;
                if (!hidPads.TryGetValue(dev, out p)) return;
                hidPads.Remove(dev);
                if (p.Preparsed != IntPtr.Zero) { Marshal.FreeHGlobal(p.Preparsed); p.Preparsed = IntPtr.Zero; }
                Disconnected(p);
            }
        }

        void EnsureTimer()
        {
            if (timerOn || hwnd == IntPtr.Zero) return;
            timerOn = SetTimer(hwnd, (IntPtr)1, 1000, IntPtr.Zero) != IntPtr.Zero;
        }

        // Mando Pro de Switch por USB: sin el saludo de USB solo manda respuestas (0x81). Si en 1,5 s no hay informes completos se le
        // pide lo de SDL / Steam (0x80 0x02 = saludo, 0x80 0x04 = USB sin tiempo limite), una vez. Por Bluetooth no hace falta
        void OnTimer()
        {
            List<Pad> todo = new List<Pad>();
            bool pending = false;
            lock (sync)
            {
                foreach (Pad p in hidPads.Values)
                {
                    if (p.HandshakeAt == DateTime.MinValue || p.HandshakeDone) continue;
                    if (p.FullReports) { p.HandshakeDone = true; continue; }
                    if (DateTime.UtcNow >= p.HandshakeAt) { p.HandshakeDone = true; todo.Add(p); }
                    else pending = true;
                }
                if (!pending) { KillTimer(hwnd, (IntPtr)1); timerOn = false; }
            }
            foreach (Pad p in todo)
            {
                IntPtr f = CreateFile(p.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (f == IntPtr.Zero || f == new IntPtr(-1)) { Say("Aviso: " + p.Name + " por USB: no se le pudo saludar (" + Marshal.GetLastWin32Error() + ")"); continue; }
                try
                {
                    byte[] b = new byte[64];
                    uint done;
                    b[0] = 0x80; b[1] = 0x02;
                    bool ok = WriteFile(f, b, 64, out done, IntPtr.Zero);
                    Thread.Sleep(30);
                    b[1] = 0x04;
                    ok = WriteFile(f, b, 64, out done, IntPtr.Zero) && ok;
                    Say("Mandos: " + p.Name + " por USB sin informes completos: saludo de USB " + (ok ? "enviado" : "fallido (" + Marshal.GetLastWin32Error() + ")"));
                }
                finally { CloseHandle(f); }
            }
        }

        void OnInput(IntPtr hRaw)
        {
            uint hdr = (uint)(8 + 2 * IntPtr.Size); // RAWINPUTHEADER
            uint size = 0;
            GetRawInputData(hRaw, RID_INPUT, IntPtr.Zero, ref size, hdr);
            if (size == 0) return;
            if (size > rawBufLen)
            {
                if (rawBuf != IntPtr.Zero) Marshal.FreeHGlobal(rawBuf);
                rawBufLen = Math.Max(size, 256);
                rawBuf = Marshal.AllocHGlobal((int)rawBufLen);
            }
            uint got = GetRawInputData(hRaw, RID_INPUT, rawBuf, ref size, hdr);
            if (got == uint.MaxValue || got < hdr) return;
            uint type = (uint)Marshal.ReadInt32(rawBuf, 0);
            IntPtr dev = Marshal.ReadIntPtr(rawBuf, 8);
            if (type == 0) { int tm = Environment.TickCount; lastMouseTick = tm == 0 ? 1 : tm; return; } // (21-11, 1.2.1: raton, solo la hora)
            if (type == 1)
            {
                { int tk = Environment.TickCount; lastKbdTick = tk == 0 ? 1 : tk; } // (21-11, 1.2.1: cualquier teclado, para el vigilante de los ganchos)
                // Teclado: solo importa el del receptor GameSir (Win derecha 0x5C / Impr Pant 0x2C = su boton de captura). La Win
                // llega siempre (el gancho solo se queda Impr Pant): con ella basta
                bool gs;
                lock (sync) if (!kbds.TryGetValue(dev, out gs)) { gs = DeviceName(dev).ToUpperInvariant().IndexOf("VID_3537") >= 0; kbds[dev] = gs; }
                if (!gs) return;
                int off = (int)hdr;
                ushort flags = (ushort)Marshal.ReadInt16(rawBuf, off + 2), vk = (ushort)Marshal.ReadInt16(rawBuf, off + 6);
                if (vk != 0x5C && vk != 0x2C) return;
                bool up = (flags & 1) != 0;
                if (!up) { kbdCaptureAt = DateTime.UtcNow; kbdCapture = true; }
                else if (vk == 0x5C || (DateTime.UtcNow - kbdCaptureAt).TotalMilliseconds > 60) kbdCapture = false;
                return;
            }
            if (type != 2) return;
            Pad p;
            lock (sync) hidPads.TryGetValue(dev, out p);
            if (p == null) return;
            int each = Marshal.ReadInt32(rawBuf, (int)hdr), count = Marshal.ReadInt32(rawBuf, (int)hdr + 4);
            if (each <= 0 || count <= 0 || hdr + 8 + (long)each * count > got) return;
            if (p.Buf.Length != each) p.Buf = new byte[each];
            for (int k = 0; k < count; k++)
            {
                Marshal.Copy(new IntPtr(rawBuf.ToInt64() + hdr + 8 + (long)k * each), p.Buf, 0, each);
                lock (sync) { uint b0 = p.Buttons; Parse(p, p.Buf, each); GuideEdge(b0, p.Buttons); } // (22-11, 1.2.2: la Guia al llegar)
            }
        }

        // ================= Lectura de los informes HID =================
        static uint HatBits(int dir) // 0 = arriba y en el sentido del reloj; otro valor = suelto
        {
            switch (dir)
            {
                case 0: return PadBtn.Up;
                case 1: return PadBtn.Up | PadBtn.Right;
                case 2: return PadBtn.Right;
                case 3: return PadBtn.Down | PadBtn.Right;
                case 4: return PadBtn.Down;
                case 5: return PadBtn.Down | PadBtn.Left;
                case 6: return PadBtn.Left;
                case 7: return PadBtn.Up | PadBtn.Left;
            }
            return 0;
        }

        static short Axis8(byte v, bool invert) // 0..255 (128 = centro, abajo = 255) -> XInput (arriba positivo)
        {
            int s = v * 257 - 32768;
            if (invert) s = -s - 1;
            return (short)Math.Max(-32768, Math.Min(32767, s));
        }

        static short Axis12(int v) // Switch: 12 bits, centro ~2048, recorrido ~±1500 (sin la calibracion del mando)
        {
            int s = (v - 2048) * 32767 / 1500;
            return (short)Math.Max(-32768, Math.Min(32767, s));
        }

        void Parse(Pad p, byte[] r, int n)
        {
            p.Reports++;
            if (p.Parser == 1) ParseDs4(p, r, n);
            else if (p.Parser == 2) ParseDualSense(p, r, n);
            else if (p.Parser == 3) { if (!ParseSwitch(p, r, n) && p.Preparsed != IntPtr.Zero) ParseDescriptor(p, r, n); }
            else ParseDescriptor(p, r, n);
        }

        // DualShock 4: USB 0x01 (64 bytes) y Bluetooth 0x01 (corto) con el mismo orden; Bluetooth completo 0x11, dos bytes despues
        static void ParseDs4(Pad p, byte[] r, int n)
        {
            int o;
            if (r[0] == 0x01 && n >= 10) o = 1;
            else if (r[0] == 0x11 && n >= 12) o = 3;
            else return;
            SonyCommon(p, r[o], r[o + 1], r[o + 2], r[o + 3], r[o + 4], r[o + 5], r[o + 6], r[o + 7], r[o + 8]);
        }

        // DualSense: USB 0x01 (64 bytes; gatillos antes que los botones), Bluetooth 0x31 (uno despues) y Bluetooth corto 0x01 (como el DS4)
        static void ParseDualSense(Pad p, byte[] r, int n)
        {
            if (r[0] == 0x01 && n >= 64) SonyCommon(p, r[1], r[2], r[3], r[4], r[8], r[9], r[10], r[5], r[6]);
            else if (r[0] == 0x31 && n >= 12) SonyCommon(p, r[2], r[3], r[4], r[5], r[9], r[10], r[11], r[6], r[7]);
            else if (r[0] == 0x01 && n >= 10) SonyCommon(p, r[1], r[2], r[3], r[4], r[5], r[6], r[7], r[8], r[9]);
        }

        static void SonyCommon(Pad p, byte lx, byte ly, byte rx, byte ry, byte b0, byte b1, byte b2, byte l2, byte r2)
        {
            uint b = HatBits(b0 & 0x0F);
            if ((b0 & 0x10) != 0) b |= PadBtn.X;     // Cuadrado
            if ((b0 & 0x20) != 0) b |= PadBtn.A;     // Cruz
            if ((b0 & 0x40) != 0) b |= PadBtn.B;     // Circulo
            if ((b0 & 0x80) != 0) b |= PadBtn.Y;     // Triangulo
            if ((b1 & 0x01) != 0) b |= PadBtn.LB;
            if ((b1 & 0x02) != 0) b |= PadBtn.RB;
            if ((b1 & 0x10) != 0) b |= PadBtn.View;  // Share / Crear
            if ((b1 & 0x20) != 0) b |= PadBtn.Start; // Options
            if ((b1 & 0x40) != 0) b |= PadBtn.L3;
            if ((b1 & 0x80) != 0) b |= PadBtn.R3;
            if ((b2 & 0x01) != 0) b |= PadBtn.Guide; // PS
            if ((b2 & 0x02) != 0) b |= PadBtn.Touchpad;
            if (p.Kind != PadKind.DualShock4)
            {
                if ((b2 & 0x04) != 0) b |= PadBtn.Mute;
                if (p.Kind == PadKind.DualSenseEdge) { if ((b2 & 0x40) != 0) b |= PadBtn.P1; if ((b2 & 0x80) != 0) b |= PadBtn.P2; }
            }
            p.Buttons = b;
            p.LX = Axis8(lx, false); p.LY = Axis8(ly, true); p.RX = Axis8(rx, false); p.RY = Axis8(ry, true);
            p.LT = l2; p.RT = r2;
        }

        // Switch (Pro, Joy-Con, mandos de terceros en modo Switch): informes completos 0x30 (y 0x21, respuestas con el estado) o el
        // sencillo 0x3F (Bluetooth sin iniciar). El Mando Pro de Switch 2 (0x09 por USB) se lee como el 0x30: solo por especificacion
        static bool ParseSwitch(Pad p, byte[] r, int n)
        {
            byte id = r[0];
            if ((id == 0x30 || id == 0x31 || id == 0x21 || (id == 0x09 && p.Kind == PadKind.Switch2Pro)) && n >= 12)
            {
                p.FullReports = true;
                byte br = r[3], bs = r[4], bl = r[5];
                uint b = 0;
                if (p.Kind != PadKind.JoyConL)
                {
                    if ((br & 0x01) != 0) b |= PadBtn.X;  // Y (izquierda)
                    if ((br & 0x02) != 0) b |= PadBtn.Y;  // X (arriba)
                    if ((br & 0x04) != 0) b |= PadBtn.A;  // B (abajo)
                    if ((br & 0x08) != 0) b |= PadBtn.B;  // A (derecha)
                    if ((br & 0x40) != 0) b |= PadBtn.RB;
                    if ((bs & 0x02) != 0) b |= PadBtn.Start;
                    if ((bs & 0x04) != 0) b |= PadBtn.R3;
                    if ((bs & 0x10) != 0) b |= PadBtn.Guide;
                }
                if (p.Kind != PadKind.JoyConR)
                {
                    if ((bl & 0x01) != 0) b |= PadBtn.Down;
                    if ((bl & 0x02) != 0) b |= PadBtn.Up;
                    if ((bl & 0x04) != 0) b |= PadBtn.Right;
                    if ((bl & 0x08) != 0) b |= PadBtn.Left;
                    if ((bl & 0x40) != 0) b |= PadBtn.LB;
                    if ((bs & 0x01) != 0) b |= PadBtn.View;
                    if ((bs & 0x08) != 0) b |= PadBtn.L3;
                    if ((bs & 0x20) != 0) b |= PadBtn.Capture;
                }
                p.Buttons = b;
                p.LT = (byte)((bl & 0x80) != 0 && p.Kind != PadKind.JoyConR ? 255 : 0); // ZL
                p.RT = (byte)((br & 0x80) != 0 && p.Kind != PadKind.JoyConL ? 255 : 0); // ZR
                if (p.Kind != PadKind.JoyConR) { p.LX = Axis12(r[6] | ((r[7] & 0x0F) << 8)); p.LY = Axis12((r[7] >> 4) | (r[8] << 4)); }
                if (p.Kind != PadKind.JoyConL) { p.RX = Axis12(r[9] | ((r[10] & 0x0F) << 8)); p.RY = Axis12((r[10] >> 4) | (r[11] << 4)); }
                return true;
            }
            if (id == 0x3F && n >= 4)
            {
                byte b1 = r[1], b2 = r[2];
                uint b = 0;
                if (p.Kind == PadKind.JoyConL)
                {
                    if ((b1 & 0x01) != 0) b |= PadBtn.Left;
                    if ((b1 & 0x02) != 0) b |= PadBtn.Down;
                    if ((b1 & 0x04) != 0) b |= PadBtn.Up;
                    if ((b1 & 0x08) != 0) b |= PadBtn.Right;
                    if ((b2 & 0x01) != 0) b |= PadBtn.View;
                    if ((b2 & 0x04) != 0) b |= PadBtn.L3;
                    if ((b2 & 0x20) != 0) b |= PadBtn.Capture;
                    if ((b2 & 0x40) != 0) b |= PadBtn.LB;
                    p.LT = (byte)((b2 & 0x80) != 0 ? 255 : 0);
                }
                else if (p.Kind == PadKind.JoyConR)
                {
                    if ((b1 & 0x01) != 0) b |= PadBtn.B;  // A
                    if ((b1 & 0x02) != 0) b |= PadBtn.Y;  // X
                    if ((b1 & 0x04) != 0) b |= PadBtn.A;  // B
                    if ((b1 & 0x08) != 0) b |= PadBtn.X;  // Y
                    if ((b2 & 0x02) != 0) b |= PadBtn.Start;
                    if ((b2 & 0x08) != 0) b |= PadBtn.R3;
                    if ((b2 & 0x10) != 0) b |= PadBtn.Guide;
                    if ((b2 & 0x40) != 0) b |= PadBtn.RB;
                    p.RT = (byte)((b2 & 0x80) != 0 ? 255 : 0);
                }
                else
                {
                    if ((b1 & 0x01) != 0) b |= PadBtn.A;  // B
                    if ((b1 & 0x02) != 0) b |= PadBtn.B;  // A
                    if ((b1 & 0x04) != 0) b |= PadBtn.X;  // Y
                    if ((b1 & 0x08) != 0) b |= PadBtn.Y;  // X
                    if ((b1 & 0x10) != 0) b |= PadBtn.LB;
                    if ((b1 & 0x20) != 0) b |= PadBtn.RB;
                    if ((b2 & 0x01) != 0) b |= PadBtn.View;
                    if ((b2 & 0x02) != 0) b |= PadBtn.Start;
                    if ((b2 & 0x04) != 0) b |= PadBtn.L3;
                    if ((b2 & 0x08) != 0) b |= PadBtn.R3;
                    if ((b2 & 0x10) != 0) b |= PadBtn.Guide;
                    if ((b2 & 0x20) != 0) b |= PadBtn.Capture;
                    p.LT = (byte)((b1 & 0x40) != 0 ? 255 : 0);
                    p.RT = (byte)((b1 & 0x80) != 0 ? 255 : 0);
                    b |= HatBits(r[3]);
                    if (n >= 12)
                    {
                        p.LX = Axis16(BitConverter.ToUInt16(r, 4), false); p.LY = Axis16(BitConverter.ToUInt16(r, 6), true);
                        p.RX = Axis16(BitConverter.ToUInt16(r, 8), false); p.RY = Axis16(BitConverter.ToUInt16(r, 10), true);
                    }
                }
                p.Buttons = b;
                return true;
            }
            return false;
        }

        // Generico: con el descriptor HID (botones de la pagina 9 por su numero; ejes X/Y, Z/Rz o Rx/Ry, cruceta = hat)
        void SetupDescriptor(Pad p)
        {
            uint n = 0;
            GetRawInputDeviceInfo(p.Dev, RIDI_PREPARSEDDATA, IntPtr.Zero, ref n);
            if (n == 0 || n > 1 << 20) return;
            p.Preparsed = Marshal.AllocHGlobal((int)n);
            if (GetRawInputDeviceInfo(p.Dev, RIDI_PREPARSEDDATA, p.Preparsed, ref n) == uint.MaxValue) { Marshal.FreeHGlobal(p.Preparsed); p.Preparsed = IntPtr.Zero; return; }
            byte[] caps = new byte[64];
            if (HidP_GetCaps(p.Preparsed, caps) != HIDP_OK) return;
            ushort nb = BitConverter.ToUInt16(caps, 46), nv = BitConverter.ToUInt16(caps, 48);
            int maxBtn = 0;
            if (nb > 0)
            {
                byte[] bc = new byte[72 * nb];
                ushort len = nb;
                if (HidP_GetButtonCaps(0, bc, ref len, p.Preparsed) == HIDP_OK)
                    for (int i = 0; i < len; i++)
                    {
                        int o = 72 * i;
                        if (BitConverter.ToUInt16(bc, o) != 9) continue;
                        bool range = bc[o + 12] != 0;
                        int max = range ? BitConverter.ToUInt16(bc, o + 58) : BitConverter.ToUInt16(bc, o + 56);
                        if (max > maxBtn) maxBtn = max;
                    }
            }
            if (nv > 0)
            {
                byte[] vc = new byte[72 * nv];
                ushort len = nv;
                if (HidP_GetValueCaps(0, vc, ref len, p.Preparsed) == HIDP_OK)
                {
                    HidVal z = null, rz = null, rx = null, ry = null, accel = null, brake = null;
                    for (int i = 0; i < len; i++)
                    {
                        int o = 72 * i;
                        ushort page = BitConverter.ToUInt16(vc, o);
                        bool range = vc[o + 12] != 0;
                        int u0 = BitConverter.ToUInt16(vc, o + 56), u1 = range ? BitConverter.ToUInt16(vc, o + 58) : u0;
                        int lmin = BitConverter.ToInt32(vc, o + 40), lmax = BitConverter.ToInt32(vc, o + 44);
                        int bits = BitConverter.ToUInt16(vc, o + 18);
                        if (lmax <= lmin && bits > 0 && bits < 32) { lmin = 0; lmax = (1 << bits) - 1; } // (sin limites en el descriptor)
                        for (int u = u0; u <= u1 && u - u0 < 32; u++)
                        {
                            HidVal v = new HidVal();
                            v.Page = page; v.Usage = (ushort)u; v.Min = lmin; v.Max = lmax; v.Signed = lmin < 0;
                            if (page == 1)
                            {
                                if (u == 0x30) p.VX = v; else if (u == 0x31) p.VY = v; else if (u == 0x32) z = v; else if (u == 0x33) rx = v;
                                else if (u == 0x34) ry = v; else if (u == 0x35) rz = v; else if (u == 0x39) p.VHat = v;
                            }
                            else if (page == 2) { if (u == 0xC4) accel = v; else if (u == 0xC5) brake = v; }
                        }
                    }
                    if (z != null && rz != null) { p.VRX = z; p.VRY = rz; p.VLT = rx ?? brake; p.VRT = ry ?? accel; }
                    else if (rx != null && ry != null) { p.VRX = rx; p.VRY = ry; if (z != null) { p.VLT = z; p.CombinedZ = rz == null; } p.VRT = rz ?? accel; if (p.VLT == null) p.VLT = brake; }
                    else { p.VLT = brake; p.VRT = accel; }
                }
            }
            p.BtnMap = GenericMap(p.Kind, maxBtn);
        }

        // Numero de boton (uso de la pagina 9, desde 1) -> PadBtn
        static uint[] GenericMap(PadKind k, int buttons)
        {
            uint A = PadBtn.A, B = PadBtn.B, X = PadBtn.X, Y = PadBtn.Y;
            if (k == PadKind.EightBitDo) // (estilo Android por la etiqueta, con las etiquetas de Nintendo: 1 = A a la derecha, 2 = B abajo, 4 = X arriba, 5 = Y a la izquierda)
                return new uint[] { 0, B, A, 0, Y, X, 0, PadBtn.LB, PadBtn.RB, TrigL, TrigR, PadBtn.View, PadBtn.Start, PadBtn.Guide, PadBtn.L3, PadBtn.R3, PadBtn.Capture };
            if (k == PadKind.GameSir || buttons >= 15) // (estilo Android con las etiquetas de Xbox: 1 = A abajo, 2 = B, 4 = X, 5 = Y)
                return new uint[] { 0, A, B, 0, X, Y, 0, PadBtn.LB, PadBtn.RB, TrigL, TrigR, PadBtn.View, PadBtn.Start, PadBtn.Guide, PadBtn.L3, PadBtn.R3, PadBtn.Capture };
            // DirectInput de siempre (Logitech, el DS4 en DirectInput): 1 = X (izquierda), 2 = A (abajo), 3 = B, 4 = Y
            return new uint[] { 0, X, A, B, Y, PadBtn.LB, PadBtn.RB, TrigL, TrigR, PadBtn.View, PadBtn.Start, PadBtn.L3, PadBtn.R3, PadBtn.Guide, PadBtn.Touchpad };
        }

        bool Value(Pad p, HidVal v, byte[] r, int n, out double norm) // 0..1
        {
            norm = 0;
            if (v == null) return false;
            int raw;
            if (v.Signed)
            {
                int s;
                if (HidP_GetScaledUsageValue(0, v.Page, 0, v.Usage, out s, p.Preparsed, r, (uint)n) != HIDP_OK) return false;
                raw = s;
            }
            else
            {
                uint u;
                if (HidP_GetUsageValue(0, v.Page, 0, v.Usage, out u, p.Preparsed, r, (uint)n) != HIDP_OK) return false;
                raw = (int)u;
            }
            norm = v.Max > v.Min ? Math.Max(0, Math.Min(1, (raw - v.Min) / (double)(v.Max - v.Min))) : 0;
            return true;
        }

        static short Stick(double norm, bool invert)
        {
            int s = (int)Math.Round(norm * 65535) - 32768;
            if (invert) s = -s - 1;
            return (short)Math.Max(-32768, Math.Min(32767, s));
        }

        void ParseDescriptor(Pad p, byte[] r, int n)
        {
            if (p.Preparsed == IntPtr.Zero || p.BtnMap == null) return;
            uint len = (uint)p.Usages.Length;
            if (HidP_GetUsages(0, 9, 0, p.Usages, ref len, p.Preparsed, r, (uint)n) == HIDP_OK)
            {
                uint b = 0;
                int lt = -1, rt = -1;
                for (int i = 0; i < len; i++)
                {
                    int u = p.Usages[i];
                    uint m = u < p.BtnMap.Length ? p.BtnMap[u] : 0;
                    if (m == TrigL) lt = 255; else if (m == TrigR) rt = 255; else b |= m;
                }
                p.Buttons = (p.Buttons & PadBtn.Dpad & (p.VHat != null ? PadBtn.Dpad : 0)) | b;
                if (p.VLT == null) p.LT = (byte)(lt > 0 ? 255 : 0);
                if (p.VRT == null) p.RT = (byte)(rt > 0 ? 255 : 0);
            }
            double[] a = p.AxisVal;
            bool[] g = p.AxisGot;
            g[0] = Value(p, p.VX, r, n, out a[0]);
            g[1] = Value(p, p.VY, r, n, out a[1]);
            g[2] = Value(p, p.VRX, r, n, out a[2]);
            g[3] = Value(p, p.VRY, r, n, out a[3]);
            g[4] = Value(p, p.VLT, r, n, out a[4]);
            g[5] = !p.CombinedZ && Value(p, p.VRT, r, n, out a[5]);
            if (p.AxisOk == null && (g[0] || g[1] || g[2] || g[3] || g[4] || g[5]))
            {
                // Con el primer informe: un stick en reposo esta en el centro y un gatillo suelto abajo. Lo que no (pedales, el acelerador
                // de un joystick de vuelo) no se usa: si no, moveria el raton o dejaria un gatillo pulsado siempre
                p.AxisOk = new bool[6];
                StringBuilder bad = new StringBuilder();
                string[] nm = { "X", "Y", "RX", "RY", "LT", "RT" };
                for (int i = 0; i < 6; i++)
                {
                    p.AxisOk[i] = !g[i] || (i < 4 || p.CombinedZ ? Math.Abs(a[i] - 0.5) < 0.2 : a[i] < 0.3);
                    if (!p.AxisOk[i]) bad.Append(bad.Length > 0 ? ", " : "").Append(nm[i]);
                }
                if (bad.Length > 0) Say("Mandos: " + p.Name + ": ejes fuera de su reposo al conectar (" + bad + "): no se usan");
            }
            bool[] ok = p.AxisOk ?? new bool[] { true, true, true, true, true, true };
            if (g[0] && ok[0]) p.LX = Stick(a[0], false);
            if (g[1] && ok[1]) p.LY = Stick(a[1], true);
            if (g[2] && ok[2]) p.RX = Stick(a[2], false);
            if (g[3] && ok[3]) p.RY = Stick(a[3], true);
            if (p.CombinedZ)
            {
                if (g[4] && ok[4]) { double v = a[4]; p.LT = (byte)(v > 0.5 ? (v - 0.5) * 510 : 0); p.RT = (byte)(v < 0.5 ? (0.5 - v) * 510 : 0); }
            }
            else
            {
                if (g[4] && ok[4]) p.LT = (byte)Math.Round(a[4] * 255);
                if (g[5] && ok[5]) p.RT = (byte)Math.Round(a[5] * 255);
            }
            if (p.VHat != null)
            {
                uint u;
                if (HidP_GetUsageValue(0, 1, 0, 0x39, out u, p.Preparsed, r, (uint)n) == HIDP_OK)
                {
                    int d = (int)u - p.VHat.Min;
                    int positions = p.VHat.Max - p.VHat.Min + 1;
                    uint hb = positions == 8 ? HatBits(d) : positions == 4 ? HatBits(d * 2) : 0;
                    p.Buttons = (p.Buttons & ~PadBtn.Dpad) | hb;
                }
            }
        }

        // --test-mandos ...;sim: un mando de mentira (como si fuera HID) con el estado que se le ponga
        static readonly IntPtr FakeDev = new IntPtr(-77);
        public void TestSet(PadKind kind, uint buttons, short lx, short ly, short rx, short ry, byte lt, byte rt)
        {
            lock (sync)
            {
                Pad p;
                if (!hidPads.TryGetValue(FakeDev, out p))
                {
                    p = new Pad();
                    p.Dev = FakeDev; p.Kind = kind; p.Name = "mando de prueba (" + PadNames.KindName(kind) + ")"; p.Source = "simulado";
                    hidPads[FakeDev] = p;
                    Connected(p);
                }
                p.Buttons = buttons; p.LX = lx; p.LY = ly; p.RX = rx; p.RY = ry; p.LT = lt; p.RT = rt;
            }
        }

        public void TestRemove()
        {
            lock (sync)
            {
                Pad p;
                if (hidPads.TryGetValue(FakeDev, out p)) { hidPads.Remove(FakeDev); Disconnected(p); }
            }
        }

        // Los lectores propios con informes hechos a mano (lo que dicen las especificaciones): OK / FALLO por caso
        public static string ParserSelfTest(out int fails)
        {
            StringBuilder sb = new StringBuilder();
            fails = 0;
            PadHub h = new PadHub();
            // DualShock 4 por USB: Cruz, L1 + Options, PS + panel tactil, L2 a 200
            byte[] r = new byte[64];
            r[0] = 0x01; r[1] = r[2] = r[3] = r[4] = 0x80; r[5] = 0x28; r[6] = 0x21; r[7] = 0x03; r[8] = 200;
            fails += Check(sb, h, PadKind.DualShock4, 1, r, PadBtn.A | PadBtn.LB | PadBtn.Start | PadBtn.Guide | PadBtn.Touchpad, 200, "DualShock 4 USB 0x01");
            // DualShock 4 por Bluetooth (0x11): Triangulo + cruceta abajo-izquierda (5)
            r = new byte[78];
            r[0] = 0x11; r[3] = r[4] = r[5] = r[6] = 0x80; r[7] = 0x85;
            fails += Check(sb, h, PadKind.DualShock4, 1, r, PadBtn.Y | PadBtn.Down | PadBtn.Left, 0, "DualShock 4 Bluetooth 0x11");
            // DualSense por USB: Cuadrado, Crear, silencio, R2 a 255
            r = new byte[64];
            r[0] = 0x01; r[1] = r[2] = r[3] = r[4] = 0x80; r[6] = 255; r[8] = 0x18; r[9] = 0x10; r[10] = 0x04;
            fails += Check(sb, h, PadKind.DualSense, 2, r, PadBtn.X | PadBtn.View | PadBtn.Mute, 0, "DualSense USB 0x01");
            // DualSense por Bluetooth (0x31): Circulo + cruceta derecha, R3
            r = new byte[78];
            r[0] = 0x31; r[2] = r[3] = r[4] = r[5] = 0x80; r[9] = 0x42; r[10] = 0x80;
            fails += Check(sb, h, PadKind.DualSense, 2, r, PadBtn.B | PadBtn.Right | PadBtn.R3, 0, "DualSense Bluetooth 0x31");
            // DualSense por Bluetooth sin iniciar (0x01 corto): Cruz, PS
            r = new byte[10];
            r[0] = 0x01; r[1] = r[2] = r[3] = r[4] = 0x80; r[5] = 0x28; r[7] = 0x01;
            fails += Check(sb, h, PadKind.DualSense, 2, r, PadBtn.A | PadBtn.Guide, 0, "DualSense Bluetooth 0x01 corto");
            // Mando Pro de Switch, informe completo 0x30: A (derecha), Captura, cruceta arriba, ZL
            r = new byte[49];
            r[0] = 0x30; r[3] = 0x08; r[4] = 0x20; r[5] = 0x82; r[6] = 0x00; r[7] = 0x08; r[8] = 0x80; r[9] = 0x00; r[10] = 0x08; r[11] = 0x80;
            fails += Check(sb, h, PadKind.SwitchPro, 3, r, PadBtn.B | PadBtn.Capture | PadBtn.Up, 255, "Mando Pro de Switch 0x30");
            // Mando Pro de Switch por Bluetooth sin iniciar (0x3F): B (abajo), HOME, + , cruceta (hat 2 = derecha)
            r = new byte[12];
            r[0] = 0x3F; r[1] = 0x01; r[2] = 0x12; r[3] = 0x02; r[5] = 0x80; r[7] = 0x80; r[9] = 0x80; r[11] = 0x80;
            fails += Check(sb, h, PadKind.SwitchPro, 3, r, PadBtn.A | PadBtn.Guide | PadBtn.Start | PadBtn.Right, 0, "Mando Pro de Switch 0x3F");
            // Joy-Con izquierdo 0x30: cruceta izquierda, -, Captura, L
            r = new byte[49];
            r[0] = 0x30; r[4] = 0x21; r[5] = 0x48; r[7] = 0x08; r[8] = 0x80;
            fails += Check(sb, h, PadKind.JoyConL, 3, r, PadBtn.Left | PadBtn.View | PadBtn.Capture | PadBtn.LB, 0, "Joy-Con izquierdo 0x30");
            sb.Append(fails == 0 ? "  lectores propios: todo OK\r\n" : "  lectores propios: " + fails + " FALLOS\r\n");
            return sb.ToString();
        }

        static int Check(StringBuilder sb, PadHub h, PadKind kind, int parser, byte[] r, uint want, byte wantLT, string what)
        {
            Pad p = new Pad();
            p.Kind = kind; p.Parser = parser;
            h.Parse(p, r, r.Length);
            bool trig = parser == 3 ? p.LT == wantLT : (wantLT == 0 || p.LT == wantLT);
            bool ok = p.Buttons == want && trig && Math.Abs((int)p.LX) < 2000 && Math.Abs((int)p.LY) < 2000;
            sb.Append("  ").Append(ok ? "OK    " : "FALLO ").Append(what).Append(": ").Append(PadNames.List(kind, p.Buttons, false))
              .Append(" (LT ").Append(p.LT).Append(", stick izq. ").Append(p.LX).Append(", ").Append(p.LY).Append(")");
            if (!ok) sb.Append(" ; se esperaba ").Append(PadNames.List(kind, want, false)).Append(" LT ").Append(wantLT);
            sb.Append("\r\n");
            return ok ? 0 : 1;
        }

        // Lo que ve Raw Input (para --test-mandos): mandos, teclados de mandos y cualquier HID con VID de mando conocido
        public string Inventory()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                foreach (RAWINPUTDEVICELIST d in DeviceList())
                {
                    uint type; ushort vid, pid, page, usage;
                    if (!DeviceInfo(d.Device, out type, out vid, out pid, out page, out usage)) continue;
                    string name = DeviceName(d.Device);
                    if (type == 1)
                    {
                        ushort kv, kp;
                        VidPidFromName(name, out kv, out kp);
                        vid = kv; pid = kp;
                    }
                    bool padUsage = type == 2 && page == 1 && (usage == 4 || usage == 5 || usage == 8);
                    bool known = vid == 0x054C || vid == 0x057E || vid == 0x045E || vid == 0x3537 || vid == 0x2DC8 || vid == 0x0F0D || vid == 0x20D6 || vid == 0x28DE;
                    if (!padUsage && !known) continue;
                    string prod = type == 2 ? ProductString(name) : null;
                    sb.Append("  Raw Input: ").Append(type == 1 ? "teclado" : type == 2 ? "HID" : "raton").Append(' ')
                      .Append(vid.ToString("X4")).Append(':').Append(pid.ToString("X4"));
                    if (type == 2) sb.Append(" pagina ").Append(page.ToString("X")).Append(" uso ").Append(usage.ToString("X"));
                    if (prod != null) sb.Append(" \"").Append(prod).Append('"');
                    sb.Append(padUsage ? (name.ToUpperInvariant().IndexOf("IG_") >= 0 ? " (mando XInput por HID: lo lee XInput)" : " (mando)") : "");
                    sb.Append("  ").Append(name).Append("\r\n");
                }
            }
            catch (Exception ex) { sb.Append("  Raw Input: error ").Append(ex.Message).Append("\r\n"); }
            try
            {
                uint n = Math.Min(16, joyGetNumDevs());
                int seen = 0;
                for (uint id = 0; id < n; id++)
                {
                    JOYINFOEX ji = new JOYINFOEX();
                    ji.Size = (uint)Marshal.SizeOf(typeof(JOYINFOEX));
                    ji.Flags = 0xFF;
                    if (joyGetPosEx(id, ref ji) != 0) continue;
                    byte[] caps = new byte[728];
                    joyGetDevCaps((UIntPtr)id, caps, 728);
                    string nm = Encoding.Unicode.GetString(caps, 4, 64);
                    int z = nm.IndexOf('\0');
                    if (z >= 0) nm = nm.Substring(0, z);
                    sb.Append("  winmm ").Append(id).Append(": ").Append(BitConverter.ToUInt16(caps, 0).ToString("X4")).Append(':')
                      .Append(BitConverter.ToUInt16(caps, 2).ToString("X4")).Append(" \"").Append(nm).Append("\", ")
                      .Append(BitConverter.ToUInt32(caps, 92)).Append(" botones\r\n");
                    seen++;
                }
                if (seen == 0) sb.Append("  winmm: ningun mando\r\n");
            }
            catch (Exception ex) { sb.Append("  winmm: error ").Append(ex.Message).Append("\r\n"); }
            return sb.ToString();
        }
    }

    // ---------------- Modo PC: el mando maneja el PC fuera de la imagen de la consola ----------------
    sealed class PcMode
    {
        public static volatile bool DryRun;            // --test-mandos: lo apunta sin mandar nada a Windows
        public static volatile Action<string> Trace;   // --test-mandos
        public static volatile bool IgnoreJoyxoff;     // --test-mandos ...;sim (el mando de mentira con Joyxoff abierto)
        public static volatile bool SimNoFullscreen;   // --test-mandos ...;sim (el guion, sea cual sea la ventana de delante)
        static PcMode running;

        // La ventana de delante, como la ve el modo PC (para --test-mandos)
        public static string ForegroundNow()
        {
            PcMode m = new PcMode(null, null, null);
            IntPtr fg = m.GetFg();
            string fs = m.FullscreenApp();
            return (fg == IntPtr.Zero ? "ninguna" : m.ExeOf(fg) ?? "?") + (fs != null ? ": a pantalla completa (el modo PC, en pausa)" : ": no es de pantalla completa (el modo PC actua)");
        }

        IntPtr GetFg() { return GetForegroundWindow(); }

        // La imagen de la consola delante, la de quien sea: mpv, el motor propio, Magpie o una ventana de la app (el telon). Con ella
        // el modo PC no hace nada (ni combos, ni vibracion, ni abrir el teclado o Playnite), tambien el residente
        static readonly string[] ImageExes = { "mpv.exe", "ConsoleEnhancerEngine.exe", "Magpie.exe" };
        static readonly uint MyPid = (uint)Process.GetCurrentProcess().Id;
        bool ImageInFront()
        {
            if (SimNoFullscreen) return false;
            IntPtr fg = GetForegroundWindow();
            return fg != IntPtr.Zero && ImageFrontHandle(fg);
        }

        bool ImageFrontHandle(IntPtr fg)
        {
            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == MyPid) return CoversMonitor(fg); // (el telon y la carga; el menu de la bandeja o un aviso de la app, no)
            string exe = ExeOf(fg);
            foreach (string e in ImageExes) if (string.Equals(e, exe, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static PcMode probe;
        // El programa de la ventana de delante es este (el residente: mpv delante = la imagen de la consola)
        public static bool ForegroundIs(string exe)
        {
            if (probe == null) probe = new PcMode(null, null, null);
            IntPtr fg = probe.GetFg();
            return fg != IntPtr.Zero && string.Equals(probe.ExeOf(fg), exe, StringComparison.OrdinalIgnoreCase);
        }

        // ¿La imagen de la consola esta delante? (mando_bloquear_joyxoff: lo pregunta el gancho por cada tecla y cada movimiento del raton,
        // asi que la respuesta se guarda por ventana de delante: solo se vuelve a calcular al cambiar la ventana o pasados 300 ms)
        public static volatile object TestForceImageFront; // (pruebas: true / false fuerza la respuesta)
        static IntPtr ifLast = (IntPtr)(-1);
        static bool ifVal;
        static int ifAt;
        public static bool ImageFrontCached()
        {
            object f = TestForceImageFront;
            if (f != null) return (bool)f;
            if (probe == null) probe = new PcMode(null, null, null);
            IntPtr fg = probe.GetFg();
            int now = Environment.TickCount;
            if (fg == ifLast && (uint)(now - ifAt) < 300) return ifVal;
            ifLast = fg;
            ifAt = now;
            ifVal = fg != IntPtr.Zero && probe.ImageFrontHandle(fg);
            return ifVal;
        }

        readonly PadHub hub;
        readonly Func<bool> consoleFront, stop;
        uint prev, held;                // botones / los que tienen su tecla o clic abajo puestos por aqui
        bool userOff;                   // apagado con el combo
        IntPtr forced;                  // ventana de pantalla completa en la que se encendio con el combo
        string state;                   // null = activo; si no, por que esta en pausa (para el registro de la prueba)
        bool stateKnown, helpLogged;
        DateTime joyAt = DateTime.MinValue;
        bool joyOpen, joyKnown;
        DateTime fsAt = DateTime.MinValue;
        IntPtr fsWnd;
        string fsApp;
        DateTime guideAt = DateTime.MinValue, comboAt = DateTime.MinValue;
        bool guideLong, comboDone, guideWhileActive;
        double ax, ay, aw, ah;
        long lastTick;
        int traceDx, traceDy, traceWheel, traceHWheel;
        DateTime traceAt = DateTime.MinValue;
        readonly Dictionary<uint, string> exeCache = new Dictionary<uint, string>();

        const double DzL = 7849, DzR = 8689; // las zonas muertas de XInput (las mismas que tenia Joyxoff)

        // Botones -> tecla (lo del perfil de Joyxoff: B = Esc, cruceta = flechas, Vista = Alt izquierda, Menu = F4)
        static readonly uint[] KeyBtn = { PadBtn.B, PadBtn.Up, PadBtn.Down, PadBtn.Left, PadBtn.Right, PadBtn.View, PadBtn.Start };
        static readonly ushort[] KeyVk = { 0x1B, 0x26, 0x28, 0x25, 0x27, 0xA4, 0x73 };
        static readonly string[] KeyName = { "Esc", "flecha arriba", "flecha abajo", "flecha izquierda", "flecha derecha", "Alt", "F4" };
        // Botones -> clic (A = izquierdo, X = derecho, R3 = central)
        static readonly uint[] ClickBtn = { PadBtn.A, PadBtn.X, PadBtn.R3 };
        static readonly uint[] ClickDown = { 0x0002, 0x0008, 0x0020 }, ClickUp = { 0x0004, 0x0010, 0x0040 };
        static readonly string[] ClickName = { "clic izquierdo", "clic derecho", "clic central" };

        PcMode(PadHub hub, Func<bool> consoleFront, Func<bool> stop)
        {
            this.hub = hub;
            this.consoleFront = consoleFront;
            this.stop = stop;
        }

        public static void Start(PadHub hub, Func<bool> consoleFront, Func<bool> stop)
        {
            if (running != null) return;
            running = new PcMode(hub, consoleFront, stop);
            Thread t = new Thread(running.Loop);
            t.IsBackground = true;
            t.Name = "Mando: modo PC";
            t.Start();
        }

        void Note(string s)
        {
            Action<string> t = Trace;
            if (t != null) t("modo PC: " + s);
        }

        void Loop()
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                bool fast = false;
                while (!stop())
                {
                    Thread.Sleep(fast ? 8 : 50);
                    long tk = sw.ElapsedTicks;
                    double dt = Math.Min(0.06, (tk - lastTick) / (double)Stopwatch.Frequency);
                    lastTick = tk;
                    fast = false;
                    if (!PadOptions.PcMode)
                    {
                        ReleaseAll();
                        SetState("apagado en el ini (mando_pc=0)");
                        prev = 0;
                        Thread.Sleep(450);
                        continue;
                    }
                    PadSnapshot s;
                    hub.Poll(out s);
                    if (s.Count == 0) { ReleaseAll(); prev = 0; Thread.Sleep(150); continue; }
                    if (!helpLogged)
                    {
                        helpLogged = true;
                        Log.Write("Modo PC del mando (mando_pc=1, sin Joyxoff): fuera de la imagen de la consola, " + PadNames.PcHelpLine(hub.MainKind) +
                                  ". En pausa en pantalla completa" + (PadOptions.FullscreenOk.Length > 0 ? " (salvo " + string.Join(", ", PadOptions.FullscreenOk) + ")" : "") +
                                  " y con Joyxoff abierto");
                    }
                    uint b = s.Buttons & ~(PadBtn.Capture | PadBtn.Touchpad | PadBtn.Mute); // (captura: la de Windows o la de la app)
                    bool ls = Mag(s.LX, s.LY) > DzL, rs = Mag(s.RX, s.RY) > DzR;
                    // Quieto: sin mirar nada mas (ni la ventana de delante ni Joyxoff)
                    if (b == 0 && prev == 0 && held == 0 && !ls && !rs) { ax = ay = aw = ah = 0; FlushTrace(true); continue; }
                    bool console = consoleFront() || ImageInFront();
                    bool joy = !console && JoyxoffOpen();
                    if (!console && !joy) Combos(b);
                    string why = userOff ? "apagado con el mando (mantener " + PadNames.Name(hub.MainKind, PadBtn.Guide, false) + " o LB+RB 1 s para encenderlo)" :
                                 console ? "la imagen de la consola esta delante (el mando es de la app)" :
                                 joy ? "Joyxoff esta abierto (lo hace Joyxoff; no se duplica)" : null;
                    if (why == null)
                    {
                        string fs = FullscreenApp();
                        if (fs != null) why = "pantalla completa: " + fs + " (mantener " + PadNames.Name(hub.MainKind, PadBtn.Guide, false) + " o LB+RB 1 s para usarlo ahi)";
                    }
                    if (why != null)
                    {
                        ReleaseAll();
                        SetState(why);
                        prev = b;
                        ax = ay = aw = ah = 0;
                        continue;
                    }
                    SetState(null);
                    Buttons(b);
                    fast = Sticks(s, dt);
                    FlushTrace(false);
                    prev = b;
                }
            }
            catch (Exception ex) { Log.Error("Modo PC del mando", ex); }
            ReleaseAll();
            FlushTrace(true);
        }

        static double Mag(short x, short y) { return Math.Sqrt((double)x * x + (double)y * y); }

        void SetState(string why)
        {
            if (stateKnown && why == state) return;
            stateKnown = true;
            state = why;
            Note(why == null ? "activo" : "en pausa: " + why);
        }

        // ---- Combos: mantener Guia (Xbox / PS / HOME) 1 s, o LB + RB 1 s: apagar / encender; pulsar Guia: el programa de mando_pc_guia ----
        void Combos(uint b)
        {
            DateTime now = DateTime.UtcNow;
            bool g = (b & PadBtn.Guide) != 0, pg = (prev & PadBtn.Guide) != 0;
            if (g && !pg) { guideAt = now; guideLong = false; guideWhileActive = state == null && stateKnown; }
            if (g && !guideLong && (now - guideAt).TotalMilliseconds >= 1000) { guideLong = true; Toggle(); }
            if (!g && pg && !guideLong && guideWhileActive && state == null && (now - guideAt).TotalMilliseconds < 1000) LaunchGuide();
            bool both = (b & (PadBtn.LB | PadBtn.RB)) == (PadBtn.LB | PadBtn.RB), pboth = (prev & (PadBtn.LB | PadBtn.RB)) == (PadBtn.LB | PadBtn.RB);
            if (both && !pboth) { comboAt = now; comboDone = false; }
            if (both && !comboDone && (now - comboAt).TotalMilliseconds >= 1000) { comboDone = true; Toggle(); }
        }

        void Toggle()
        {
            if (userOff)
            {
                userOff = false;
                Log.Write("Modo PC del mando encendido (con el mando)");
                Note("encendido con el mando");
                hub.Rumble(1);
                return;
            }
            string fs = FullscreenApp();
            if (fs != null)
            {
                forced = GetForegroundWindow();
                fsAt = DateTime.MinValue;
                Log.Write("Modo PC del mando: tambien en esta ventana de pantalla completa (" + fs + ")");
                Note("activo tambien en pantalla completa: " + fs);
                hub.Rumble(1);
                return;
            }
            userOff = true;
            ReleaseAll();
            Log.Write("Modo PC del mando apagado (con el mando; mantener la Guia o LB+RB 1 s para encenderlo)");
            Note("apagado con el mando");
            hub.Rumble(2);
        }

        // ---- Botones ----
        void Buttons(uint b)
        {
            uint down = b & ~prev, up = prev & ~b;
            for (int i = 0; i < KeyBtn.Length; i++)
            {
                uint k = KeyBtn[i];
                if ((down & k) != 0 && (held & k) == 0) { Key(KeyVk[i], false); held |= k; Note(KeyName[i] + " (abajo)"); }
                else if ((up & k) != 0 && (held & k) != 0) { Key(KeyVk[i], true); held &= ~k; }
            }
            for (int i = 0; i < ClickBtn.Length; i++)
            {
                uint k = ClickBtn[i];
                if ((down & k) != 0 && (held & k) == 0) { Mouse(ClickDown[i], 0); held |= k; Note(ClickName[i] + " (abajo)"); }
                else if ((up & k) != 0 && (held & k) != 0) { Mouse(ClickUp[i], 0); held &= ~k; }
            }
            if ((down & PadBtn.L3) != 0) ToggleOsk();
        }

        void ReleaseAll()
        {
            if (held == 0) return;
            for (int i = 0; i < KeyBtn.Length; i++) if ((held & KeyBtn[i]) != 0) Key(KeyVk[i], true);
            for (int i = 0; i < ClickBtn.Length; i++) if ((held & ClickBtn[i]) != 0) Mouse(ClickUp[i], 0);
            held = 0;
        }

        // ---- Sticks: raton (curva al cuadrado: preciso despacio; a tope, mando_pc_velocidad x el alto de la pantalla por segundo) y rueda ----
        bool Sticks(PadSnapshot s, double dt)
        {
            bool moving = false;
            double m = Mag(s.LX, s.LY);
            if (m > DzL)
            {
                double n = Math.Min(1, (m - DzL) / (32767 - DzL));
                double v = PadOptions.Speed * Math.Max(480, GetSystemMetrics(1)) * n * n;
                ax += s.LX / m * v * dt;
                ay -= s.LY / m * v * dt;
                int ix = (int)ax, iy = (int)ay;
                ax -= ix; ay -= iy;
                if (ix != 0 || iy != 0) MoveBy(ix, iy);
                moving = true;
            }
            else { ax = ay = 0; }
            double r = Mag(s.RX, s.RY);
            if (r > DzR)
            {
                double n = Math.Min(1, (r - DzR) / (32767 - DzR));
                double rate = 1200 * n * n; // (a tope, 10 golpes de rueda por segundo)
                aw += s.RY / r * rate * dt;
                ah += s.RX / r * rate * dt;
                if (Math.Abs(aw) >= 30) { int d = (int)aw; aw -= d; Mouse(0x0800, d); }
                if (Math.Abs(ah) >= 30) { int d = (int)ah; ah -= d; Mouse(0x1000, d); }
                moving = true;
            }
            else { aw = ah = 0; }
            return moving;
        }

        void MoveBy(int dx, int dy)
        {
            if (DryRun) { traceDx += dx; traceDy += dy; return; }
            POINT p;
            if (!GetCursorPos(out p)) return;
            int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
            int x = Math.Max(vx, Math.Min(vx + vw - 1, p.X + dx)), y = Math.Max(vy, Math.Min(vy + vh - 1, p.Y + dy));
            SetCursorPos(x, y);
        }

        void FlushTrace(bool now)
        {
            if (Trace == null || (traceDx == 0 && traceDy == 0 && traceWheel == 0 && traceHWheel == 0)) return;
            if (!now && (DateTime.UtcNow - traceAt).TotalMilliseconds < 500) return;
            traceAt = DateTime.UtcNow;
            Note("raton " + traceDx + ", " + traceDy + " px; rueda " + traceWheel + " / " + traceHWheel + " (lo que mandaria)");
            traceDx = traceDy = traceWheel = traceHWheel = 0;
        }

        // ---- Mandar a Windows (marcado: el gancho del teclado no lo toma por el mando) ----
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTU { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTU u; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        static readonly IntPtr Mark = (IntPtr)0x50434D44; // "PCMD"

        void Key(ushort vk, bool up)
        {
            if (DryRun) { if (!up) Note("tecla " + vk.ToString("X2") + " (lo que mandaria)"); return; }
            INPUT[] i = new INPUT[1];
            i[0].type = 1;
            i[0].u.ki.wVk = vk;
            i[0].u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
            i[0].u.ki.dwFlags = (up ? 2u : 0u) | (vk >= 0x21 && vk <= 0x2E ? 1u : 0u); // (flechas: teclas extendidas)
            i[0].u.ki.dwExtraInfo = Mark;
            SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
        }

        void Mouse(uint flags, int data)
        {
            if (DryRun)
            {
                if (flags == 0x0800) traceWheel += data; else if (flags == 0x1000) traceHWheel += data;
                return;
            }
            INPUT[] i = new INPUT[1];
            i[0].type = 0;
            i[0].u.mi.dwFlags = flags;
            i[0].u.mi.mouseData = unchecked((uint)data);
            i[0].u.mi.dwExtraInfo = Mark;
            SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
        }

        // L3: el teclado en pantalla de Windows (osk.exe) se abre o se cierra
        void ToggleOsk()
        {
            IntPtr w = FindWindow("OSKMainClass", null);
            if (DryRun) { Note(w != IntPtr.Zero ? "cerraria el teclado en pantalla" : "abriria el teclado en pantalla (osk.exe)"); return; }
            try
            {
                if (w != IntPtr.Zero) PostMessage(w, 0x0112, (IntPtr)0xF060, IntPtr.Zero); // WM_SYSCOMMAND SC_CLOSE
                else Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "osk.exe")) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write("Modo PC del mando: no se pudo abrir el teclado en pantalla (" + ex.Message + ")"); }
        }

        void LaunchGuide()
        {
            string app = PadOptions.GuideApp;
            if (app.Length == 0) return;
            if (DryRun) { Note("abriria " + app); return; }
            try
            {
                if (!File.Exists(app)) { Log.Write("Modo PC del mando: no existe " + app + " (mando_pc_guia)"); return; }
                ProcessStartInfo si = new ProcessStartInfo(app);
                si.UseShellExecute = true;
                si.WorkingDirectory = System.IO.Path.GetDirectoryName(app);
                Process.Start(si);
                Log.Write("Modo PC del mando: abierto " + System.IO.Path.GetFileName(app) + " (Guia)");
            }
            catch (Exception ex) { Log.Write("Modo PC del mando: no se pudo abrir " + app + " (" + ex.Message + ")"); }
        }

        // ---- Joyxoff abierto: no se hace nada (cada 5 s, solo si se esta tocando el mando) ----
        bool JoyxoffOpen()
        {
            if (IgnoreJoyxoff) return false;
            DateTime now = DateTime.UtcNow;
            if ((now - joyAt).TotalSeconds < 5) return joyOpen;
            joyAt = now;
            bool open = false;
            try
            {
                Process[] ps = Process.GetProcessesByName("Joyxoff");
                open = ps.Length > 0;
                foreach (Process p in ps) p.Dispose();
            }
            catch { }
            if (open != joyOpen || !joyKnown)
            {
                if (open) Log.Write("Modo PC del mando: Joyxoff esta abierto, asi que no actua (para no duplicar el raton y las teclas); al cerrar Joyxoff lo hace la app");
                else if (joyKnown) Log.Write("Modo PC del mando: Joyxoff cerrado, ahora lo hace la app");
                joyOpen = open;
                joyKnown = true;
            }
            return joyOpen;
        }

        // ---- Ventana de delante a pantalla completa (sin marco y tapando su pantalla: juegos, mpv, el telon) -> en pausa ----
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder sb, ref uint n);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        string FullscreenApp()
        {
            if (SimNoFullscreen) return null;
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return null;
            DateTime now = DateTime.UtcNow;
            if (fg == fsWnd && (now - fsAt).TotalMilliseconds < 500) return fsApp;
            fsWnd = fg;
            fsAt = now;
            fsApp = null;
            if (fg == forced) return null;
            RECT r;
            MONITORINFO mi = new MONITORINFO();
            mi.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetWindowRect(fg, out r) || !GetMonitorInfo(MonitorFromWindow(fg, 2), ref mi)) return null;
            if (r.L > mi.Monitor.L || r.T > mi.Monitor.T || r.R < mi.Monitor.R || r.B < mi.Monitor.B) return null;
            if ((GetWindowLong(fg, -16) & 0x00C00000) == 0x00C00000) return null; // (con barra de titulo: maximizada, no pantalla completa)
            StringBuilder cls = new StringBuilder(64);
            GetClassName(fg, cls, 64);
            if (cls.ToString() == "Progman" || cls.ToString() == "WorkerW") return null; // (el escritorio)
            string exe = ExeOf(fg) ?? "?";
            foreach (string ok in PadOptions.FullscreenOk)
                if (string.Equals(ok, exe, StringComparison.OrdinalIgnoreCase)) return null;
            fsApp = exe;
            return fsApp;
        }

        static bool CoversMonitor(IntPtr w)
        {
            RECT r;
            MONITORINFO mi = new MONITORINFO();
            mi.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetWindowRect(w, out r) || !GetMonitorInfo(MonitorFromWindow(w, 2), ref mi)) return false;
            return r.L <= mi.Monitor.L && r.T <= mi.Monitor.T && r.R >= mi.Monitor.R && r.B >= mi.Monitor.B;
        }

        string ExeOf(IntPtr w)
        {
            uint pid;
            GetWindowThreadProcessId(w, out pid);
            string s;
            if (exeCache.TryGetValue(pid, out s)) return s;
            s = null;
            IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h != IntPtr.Zero)
            {
                try
                {
                    StringBuilder sb = new StringBuilder(520);
                    uint n = 520;
                    if (QueryFullProcessImageName(h, 0, sb, ref n)) s = System.IO.Path.GetFileName(sb.ToString());
                }
                finally { CloseHandle(h); }
            }
            if (exeCache.Count > 200) exeCache.Clear();
            exeCache[pid] = s;
            return s;
        }
    }

    // ---------------- Enganches con la app (TrayApp) ----------------
    partial class TrayApp
    {
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);

        // ---- Bloquear a Joyxoff con la imagen de la consola delante (mando_bloquear_joyxoff) ----
        // Con la imagen delante, el gancho de teclado (KeyHook, mas abajo) y uno nuevo del raton (MouseHook) se tragan la entrada
        // INYECTADA (SendInput / keybd_event / mouse_event: lo que manda Joyxoff) que no sea de la propia app. La entrada FISICA (teclado,
        // raton del usuario, y el boton del GameSir, que es un teclado de verdad) pasa siempre. Asi Joyxoff sigue manejando el PC pero no
        // toca nada mientras se juega, aunque su "auto-desactivar en pantalla completa" no lo pille (mpv / el motor son ventanas sin borde).
        // Nota: un gancho de bajo nivel ve y puede tragarse (devolviendo 1) la entrada inyectada por un proceso de mas privilegios (Joyxoff
        // va elevado); el marcador LLKHF_LOWER_IL_INJECTED solo distingue a los de MENOS privilegios, y aqui no se usa: basta con INJECTED
        internal const long AppMarkPcMode = 0x50434D44;   // "PCMD": lo inyecta el modo PC (PcMode)
        internal const long AppMarkTest = 0x5332;         // pruebas (como en KeyHook)
        internal static readonly UIntPtr FillerMark = (UIntPtr)0x50434D44; // el 0xE8 del gancho del GameSir, marcado para que pase

        static bool IsAppInjected(IntPtr extra)
        {
            long e = extra.ToInt64();
            return e == AppMarkPcMode || e == AppMarkTest;
        }

        // La regla que usan los dos ganchos (y la prueba --test-bloqueo): tragarse la entrada inyectada que no sea de la app, con la
        // imagen delante y mando_bloquear_joyxoff=1. injected = el bit INJECTED del evento (0x10 teclado, 0x01 raton)
        internal static bool ShouldSwallowInjected(bool injected, IntPtr extra)
        {
            return injected && !IsAppInjected(extra) && PadOptions.BlockJoyxoff && PcMode.ImageFrontCached();
        }

        [StructLayout(LayoutKind.Sequential)] struct MsLL { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
        LowLevelKeyboardProc mouseProc; // (la misma firma vale para WH_MOUSE_LL; referenciado para que no lo recoja el GC)
        IntPtr mouseHook;

        void InstallMouseHook()
        {
            if (!PadOptions.BlockJoyxoff) return; // (mando_bloquear_joyxoff=0: ni se instala)
            mouseProc = MouseHook;
            mouseHook = SetWindowsHookEx(14, mouseProc, GetModuleHandle(null), 0); // WH_MOUSE_LL, en el hilo de la interfaz
            // (21-11, 1.2.1: el aviso solo la primera vez y si falla; el vigilante lo vuelve a poner a veces y el log lo decia cada 2 s)
            if (mouseHook == IntPtr.Zero) Log.Write("Aviso: no se pudo instalar el gancho del raton para bloquear Joyxoff con la imagen delante (" + Marshal.GetLastWin32Error() + ")");
            else if (!bloqueoDicho) { bloqueoDicho = true; Log.Write("Bloqueo de Joyxoff con la imagen delante (mando_bloquear_joyxoff=1): activo (teclado y raton)"); }
            ganchoRatonUlt = Environment.TickCount;
        }
        bool bloqueoDicho;

        IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            ganchoUlt = Environment.TickCount;   // (1.0.12: vigilante de los ganchos, MenuJuego.cs GanchosVigila)
            ganchoRatonUlt = ganchoUlt;          // (21-11, 1.2.1: el de cada gancho, frente a lo que ve Raw Input)
            try
            {
                if (nCode >= 0 && PadOptions.BlockJoyxoff)
                {
                    MsLL m = (MsLL)Marshal.PtrToStructure(lParam, typeof(MsLL));
                    // LLMHF_INJECTED = 0x01. El raton fisico no lo lleva: pasa siempre. Lo de la app (marca) tambien
                    if (ShouldSwallowInjected((m.Flags & 0x01) != 0, m.Extra)) return (IntPtr)1;
                }
            }
            catch { }
            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        void ReinstallMouseHook()
        {
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
            InstallMouseHook();
        }

        // Captura / panel tactil de un mando HID (Switch, 8BitDo, PlayStation) o el boton de captura del GameSir visto por Raw Input:
        // la generacion de fotogramas, como hace el gancho del teclado (KeyHook) con el del GameSir. kbd: el del GameSir, 0,2 s despues
        // de verlo: si el gancho no lo ha hecho (Windows quita los ganchos que tardan), aqui, y el gancho se vuelve a poner
        void PadCaptureButton(bool kbd)
        {
            IntPtr w = mpvWindow;
            if (w == IntPtr.Zero || GetForegroundWindow() != w) return;
            if ((DateTime.Now - lastShotKey).TotalMilliseconds < (kbd ? 1000 : 600)) return;
            lastShotKey = DateTime.Now;
            if (kbd)
            {
                Log.Write("Boton de captura del GameSir visto por Raw Input sin el gancho del teclado: la generacion desde aqui y se vuelven a poner los ganchos");
                OnUi(ReinstallKeyHook);
                OnUi(ReinstallMouseHook); // (si Windows quito el del teclado por lento, seguramente tambien el del raton)
            }
            ThreadPool.QueueUserWorkItem(delegate { ToggleFrameGeneration(); });
        }

        void ReinstallKeyHook()
        {
            if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
            keyHook = IntPtr.Zero;
            InstallKeyHook();
        }

        // Modo PC (src\Mandos.cs): "la imagen de la consola delante" es la ventana de mpv de delante (lo mismo que mira KeyHook)
        void StartPcMode(PadHub hub)
        {
            PcMode.Start(hub, delegate { IntPtr w = mpvWindow; return w != IntPtr.Zero && GetForegroundWindow() == w; }, delegate { return userExit; });
        }
    }

    // ---------------- Residente: Switch2Magpie.exe --mando-pc ----------------
    // El modo PC tambien con la app cerrada (la app se cierra al apagar la consola): un proceso sin ventanas, uno solo, para ponerlo al
    // iniciar Windows en lugar de Joyxoff. Mientras la app este abierta no hace nada (lo hace la de la app) y con mpv delante tampoco
    static class PadResident
    {
        const string MutexName = "Local\\ConsoleEnhancer_MandoPC";

        public static int Run()
        {
            bool created;
            using (Mutex m = new Mutex(true, MutexName, out created))
            {
                if (!created) return 0;
                try { Config.Load(); } catch (Exception ex) { Log.Error("Modo PC residente: ini", ex); }
                Log.Write("Modo PC del mando residente (--mando-pc): el mando maneja el PC tambien con la app cerrada (" + PadOptions.Describe() + ")");
                PadHub.Shared.Start();
                DateTime appAt = DateTime.MinValue;
                bool app = false;
                PcMode.Start(PadHub.Shared, delegate
                {
                    // (la app abierta: lo hace ella; se mira cada 2 s, y solo si se toca el mando)
                    if ((DateTime.UtcNow - appAt).TotalSeconds >= 2)
                    {
                        appAt = DateTime.UtcNow;
                        Mutex o;
                        app = Mutex.TryOpenExisting("Local\\ConsoleEnhancer_SingleInstance", out o);
                        if (o != null) o.Dispose();
                    }
                    return app || PcMode.ForegroundIs("mpv.exe");
                }, delegate { return false; });
                Thread.Sleep(Timeout.Infinite);
            }
            return 0;
        }
    }

    // ---------------- Prueba del bloqueo de Joyxoff: --test-bloqueo=<archivo> ----------------
    // Sin la capturadora ni mando: dos ganchos de bajo nivel, uno "centinela" (cuenta lo que le llega) y encima el de la app (la regla
    // TrayApp.ShouldSwallowInjected). Se inyecta (SendInput) teclado y raton y se mira si el centinela lo recibe o la app se lo tragó, con
    // la imagen delante / sin ella, con marca de la app / sin ella, y con mando_bloquear_joyxoff 1 / 0. Si el inyector no va elevado (como
    // Joyxoff), al menos prueba que el mecanismo del gancho traga lo inyectado; la decision es la misma con un proceso elevado (ver KeyHook)
    static class BlockTest
    {
        delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [StructLayout(LayoutKind.Sequential)] struct KbdLL { public uint Vk, Scan, Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct MsLL { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr w, l; public uint time; public int x, y; }
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG m, IntPtr h, uint a, uint b, uint remove);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [StructLayout(LayoutKind.Sequential)] struct MInput { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KInput { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct U { [FieldOffset(0)] public MInput mi; [FieldOffset(0)] public KInput ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public U u; }
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] a, int size);

        static HookProc bKbd, bMouse, sKbd, sMouse;
        static IntPtr hbK, hbM, hsK, hsM;
        static int sentKbd, sentMouse;

        public static int Run(string file, string[] args)
        {
            foreach (string a in args) if (a.StartsWith("--ini=") && File.Exists(a.Substring(6))) Paths.IniFile = Path.GetFullPath(a.Substring(6));
            StringBuilder sb = new StringBuilder();
            bool ok = true;
            Action<string> W = delegate (string s) { sb.Append(s).Append("\r\n"); };
            try
            {
                try { Config.Load(); } catch { }
                PadOptions.BlockJoyxoff = true;
                W("Prueba del bloqueo de Joyxoff (mando_bloquear_joyxoff). " + PadOptions.Describe());
                bool admin = false;
                try { admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); } catch { }
                W("El proceso de la prueba va " + (admin ? "ELEVADO" : "sin elevar") + " (Joyxoff va elevado; el inyector de la prueba va como este proceso)");
                // centinela primero (se llama despues), bloqueador encima (se llama antes)
                sKbd = SentKbd; sMouse = SentMouse; bKbd = BlockKbd; bMouse = BlockMouse;
                IntPtr mod = GetModuleHandle(null);
                hsK = SetWindowsHookEx(13, sKbd, mod, 0);
                hsM = SetWindowsHookEx(14, sMouse, mod, 0);
                hbK = SetWindowsHookEx(13, bKbd, mod, 0);
                hbM = SetWindowsHookEx(14, bMouse, mod, 0);
                if (hsK == IntPtr.Zero || hsM == IntPtr.Zero || hbK == IntPtr.Zero || hbM == IntPtr.Zero)
                { W("ERROR: no se pudieron instalar los ganchos (" + Marshal.GetLastWin32Error() + ")"); ok = false; }
                else
                {
                    ok &= Case(W, "imagen delante, inyectado sin marca", true, false, false, false);     // se traga
                    ok &= Case(W, "imagen delante, inyectado con la marca de la app", true, true, true, true);
                    ok &= Case(W, "SIN imagen delante, inyectado sin marca", false, false, true, true);  // pasa
                    PadOptions.BlockJoyxoff = false;
                    ok &= Case(W, "mando_bloquear_joyxoff=0, imagen delante, inyectado sin marca", true, false, true, true);
                    PadOptions.BlockJoyxoff = true;
                }
            }
            catch (Exception ex) { W("ERROR: " + ex); ok = false; }
            finally
            {
                PcMode.TestForceImageFront = null;
                if (hbK != IntPtr.Zero) UnhookWindowsHookEx(hbK);
                if (hbM != IntPtr.Zero) UnhookWindowsHookEx(hbM);
                if (hsK != IntPtr.Zero) UnhookWindowsHookEx(hsK);
                if (hsM != IntPtr.Zero) UnhookWindowsHookEx(hsM);
                W(ok ? "RESULTADO: todo como se esperaba" : "RESULTADO: algo FALLÓ (ver arriba)");
                try { File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false)); } catch { }
            }
            return ok ? 0 : 1;
        }

        static bool Case(Action<string> W, string what, bool imageFront, bool marked, bool wantKbd, bool wantMouse)
        {
            PcMode.TestForceImageFront = imageFront;
            sentKbd = sentMouse = 0;
            IntPtr mark = marked ? (IntPtr)0x50434D44 : (IntPtr)0x1234; // 0x1234 no es marca de la app
            InjectKey(0x41, mark); // 'A'
            InjectMouseMove(mark);
            Pump(120);
            bool okK = (sentKbd > 0) == wantKbd, okM = (sentMouse > 0) == wantMouse;
            W((okK && okM ? "OK   " : "FALLO") + " " + what + ": teclado " + (sentKbd > 0 ? "pasa" : "se traga") +
              " (se esperaba " + (wantKbd ? "pasa" : "se traga") + "), raton " + (sentMouse > 0 ? "pasa" : "se traga") +
              " (se esperaba " + (wantMouse ? "pasa" : "se traga") + ")");
            return okK && okM;
        }

        static IntPtr BlockKbd(int n, IntPtr w, IntPtr l)
        {
            if (n >= 0) { KbdLL k = (KbdLL)Marshal.PtrToStructure(l, typeof(KbdLL)); if (TrayApp.ShouldSwallowInjected((k.Flags & 0x10) != 0, k.Extra)) return (IntPtr)1; }
            return CallNextHookEx(hbK, n, w, l);
        }
        static IntPtr BlockMouse(int n, IntPtr w, IntPtr l)
        {
            if (n >= 0) { MsLL m = (MsLL)Marshal.PtrToStructure(l, typeof(MsLL)); if (TrayApp.ShouldSwallowInjected((m.Flags & 0x01) != 0, m.Extra)) return (IntPtr)1; }
            return CallNextHookEx(hbM, n, w, l);
        }
        static IntPtr SentKbd(int n, IntPtr w, IntPtr l)
        {
            if (n >= 0 && (w.ToInt64() == 0x100 || w.ToInt64() == 0x104)) { KbdLL k = (KbdLL)Marshal.PtrToStructure(l, typeof(KbdLL)); if (k.Vk == 0x41) sentKbd++; }
            return CallNextHookEx(hsK, n, w, l);
        }
        static IntPtr SentMouse(int n, IntPtr w, IntPtr l)
        {
            if (n >= 0 && w.ToInt64() == 0x200) sentMouse++; // WM_MOUSEMOVE
            return CallNextHookEx(hsM, n, w, l);
        }

        static void InjectKey(ushort vk, IntPtr mark)
        {
            INPUT[] a = new INPUT[2];
            a[0].type = 1; a[0].u.ki.vk = vk; a[0].u.ki.extra = mark;
            a[1].type = 1; a[1].u.ki.vk = vk; a[1].u.ki.flags = 2; a[1].u.ki.extra = mark; // KEYUP
            SendInput(2, a, Marshal.SizeOf(typeof(INPUT)));
        }
        static void InjectMouseMove(IntPtr mark)
        {
            INPUT[] a = new INPUT[1];
            a[0].type = 0; a[0].u.mi.dx = 3; a[0].u.mi.dy = 0; a[0].u.mi.flags = 0x0001; a[0].u.mi.extra = mark; // MOUSEEVENTF_MOVE
            SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
        }
        static void Pump(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                MSG m;
                while (PeekMessage(out m, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessage(ref m); }
                Thread.Sleep(2);
            }
        }
    }

    // ---------------- Prueba: --test-mandos=<segundos>;<archivo> ----------------
    static class PadTest
    {
        public static int Run(string spec, string[] args)
        {
            string[] f = spec.Split(new char[] { ';' }, 3);
            double secs;
            if (!double.TryParse(f[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out secs) || secs <= 0) secs = 20;
            string file = f.Length > 1 && f[1].Trim().Length > 0 ? f[1].Trim() : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "test_mandos.txt");
            bool sim = f.Length > 2 && f[2].Trim().ToLowerInvariant() == "sim";
            if (sim) secs = Math.Max(secs, 11);
            foreach (string a in args)
                if (a.StartsWith("--ini=") && File.Exists(a.Substring(6))) Paths.IniFile = System.IO.Path.GetFullPath(a.Substring(6));
            StringBuilder sb = new StringBuilder();
            object sync = new object();
            Stopwatch clock = Stopwatch.StartNew();
            Action<string> W = delegate (string line)
            {
                lock (sync) sb.Append(clock.Elapsed.TotalSeconds.ToString("000.000", CultureInfo.InvariantCulture)).Append("  ").Append(line).Append("\r\n");
            };
            Log.Hook = delegate (string m) { W("[log] " + m); };
            PadHub.LogFn = delegate (string m) { W("[mandos] " + m); };
            try
            {
                W("Prueba de mandos (" + secs.ToString("0.#", CultureInfo.InvariantCulture) + " s). ini: " + Paths.IniFile);
                try { Config.Load(); } catch (Exception ex) { W("ini: " + ex.Message); }
                W("Claves: " + PadOptions.Describe());
                Process me = Process.GetCurrentProcess();
                TimeSpan cpu0 = me.TotalProcessorTime;
                PadHub hub = PadHub.Shared;
                hub.Start();
                Thread.Sleep(800); // (las llegadas de Raw Input de lo que ya esta conectado)
                W(hub.RawInputOk ? "Raw Input: registrado (joystick, gamepad, multieje y teclado, sin foco)" : "Raw Input: NO registrado");
                W(hub.MeasureXInput());
                W("Dispositivos:\r\n" + hub.Inventory().TrimEnd());
                PadSnapshot s;
                Stopwatch pw = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) hub.Poll(out s);
                W("PadHub.Poll: " + (pw.Elapsed.TotalMilliseconds * 10).ToString("0", CultureInfo.InvariantCulture) + " us por lectura (100 seguidas)");
                List<Pad> pads = hub.Pads();
                W("Mandos detectados: " + pads.Count);
                foreach (Pad p in pads)
                    W("  " + p.Describe() + " -> " + PadNames.KindName(p.Kind) + "; botones: " + PadNames.List(p.Kind, PadNames.Has(p.Kind), false) + ", " +
                      PadNames.TriggerName(p.Kind, true) + "/" + PadNames.TriggerName(p.Kind, false));
                bool joy = Process.GetProcessesByName("Joyxoff").Length > 0;
                W("Joyxoff: " + (joy ? "abierto (el modo PC no actua mientras siga abierto)" : "cerrado"));
                int pf;
                W("Lectores HID propios con informes hechos a mano:\r\n" + PadHub.ParserSelfTest(out pf).TrimEnd());
                PcMode.DryRun = true; // (sin mover el raton ni pulsar nada de verdad)
                PcMode.Trace = W;
                if (sim)
                {
                    // Un mando de mentira (Xbox) con un guion: lo que hace el modo PC con cada cosa (sin mandar nada a Windows)
                    PcMode.IgnoreJoyxoff = true;
                    W("Ventana de delante de verdad: " + PcMode.ForegroundNow());
                    PcMode.SimNoFullscreen = true;
                    W("Simulacion: mando de mentira; en la simulacion no se miran ni Joyxoff ni la pantalla completa");
                    Thread th = new Thread(delegate ()
                    {
                        object[][] g = {
                            new object[] { 0.5, "A (clic izquierdo)", PadBtn.A, 0, 0 },
                            new object[] { 0.8, "suelto", 0u, 0, 0 },
                            new object[] { 1.0, "stick izquierdo a tope a la derecha 0,5 s", 0u, 32767, 0 },
                            new object[] { 1.5, "suelto", 0u, 0, 0 },
                            new object[] { 1.7, "stick izquierdo a medias arriba 0,5 s", 0u, 0, 1 },
                            new object[] { 2.2, "suelto", 0u, 0, 0 },
                            new object[] { 2.4, "stick derecho arriba 0,5 s (rueda)", 0u, 0, 2 },
                            new object[] { 2.9, "suelto", 0u, 0, 0 },
                            new object[] { 3.1, "Vista (Alt)", PadBtn.View, 0, 0 },
                            new object[] { 3.2, "Vista + Menu (Alt+F4)", PadBtn.View | PadBtn.Start, 0, 0 },
                            new object[] { 3.4, "suelto", 0u, 0, 0 },
                            new object[] { 3.6, "cruceta abajo, B", PadBtn.Down, 0, 0 },
                            new object[] { 3.8, "B", PadBtn.B, 0, 0 },
                            new object[] { 3.9, "suelto", 0u, 0, 0 },
                            new object[] { 4.2, "Guia mantenida 1,3 s (apagar)", PadBtn.Guide, 0, 0 },
                            new object[] { 5.5, "suelto", 0u, 0, 0 },
                            new object[] { 5.8, "A con el modo PC apagado (nada)", PadBtn.A, 0, 0 },
                            new object[] { 6.0, "suelto", 0u, 0, 0 },
                            new object[] { 6.3, "LB + RB 1,2 s (encender)", PadBtn.LB | PadBtn.RB, 0, 0 },
                            new object[] { 7.5, "suelto", 0u, 0, 0 },
                            new object[] { 7.8, "Guia pulsada (abrir el programa)", PadBtn.Guide, 0, 0 },
                            new object[] { 8.0, "suelto", 0u, 0, 0 },
                            new object[] { 8.3, "L3 (teclado en pantalla)", PadBtn.L3, 0, 0 },
                            new object[] { 8.5, "X (clic derecho), R3 (central)", PadBtn.X | PadBtn.R3, 0, 0 },
                            new object[] { 8.7, "suelto", 0u, 0, 0 },
                            new object[] { 9.5, "se quita el mando de mentira", 0u, 0, -1 } };
                        double t0 = clock.Elapsed.TotalSeconds;
                        foreach (object[] st in g)
                        {
                            double at = t0 + (double)st[0];
                            while (clock.Elapsed.TotalSeconds < at) Thread.Sleep(5);
                            W("simulacion: " + (string)st[1]);
                            int stick = (int)st[4];
                            if (stick < 0) { PadHub.Shared.TestRemove(); continue; }
                            short lx = (short)(int)st[3], ly = (short)(stick == 1 ? 20000 : 0), ry = (short)(stick == 2 ? 32767 : 0);
                            PadHub.Shared.TestSet(PadKind.Xbox, Convert.ToUInt32(st[2]), lx, ly, 0, ry, 0, 0);
                        }
                    });
                    th.IsBackground = true;
                    th.Start();
                }
                PcMode.Start(hub, delegate { return false; }, delegate { return clock.Elapsed.TotalSeconds >= secs + 1; });
                cpu0 = me.TotalProcessorTime;
                Stopwatch loop = Stopwatch.StartNew();
                Dictionary<string, Pad> last = new Dictionary<string, Pad>();
                int polls = 0;
                while (clock.Elapsed.TotalSeconds < secs + 1)
                {
                    Thread.Sleep(50);
                    hub.Poll(out s);
                    polls++;
                    Dictionary<string, Pad> now = new Dictionary<string, Pad>();
                    foreach (Pad p in hub.Pads())
                    {
                        string key = p.Source + p.Dev + p.XIndex + p.JoyId;
                        now[key] = p;
                        Pad o;
                        if (!last.TryGetValue(key, out o)) { W("conectado: " + p.Describe()); o = new Pad(); }
                        uint dn = p.Buttons & ~o.Buttons, upb = o.Buttons & ~p.Buttons;
                        if (dn != 0) W("+ " + PadNames.List(p.Kind, dn, false) + "   [" + p.Name + "]");
                        if (upb != 0) W("- " + PadNames.List(p.Kind, upb, false) + "   [" + p.Name + "]");
                        if ((p.LT > 140) != (o.LT > 140)) W((p.LT > 140 ? "+ " : "- ") + PadNames.TriggerName(p.Kind, true) + " (" + p.LT + ")   [" + p.Name + "]");
                        if ((p.RT > 140) != (o.RT > 140)) W((p.RT > 140 ? "+ " : "- ") + PadNames.TriggerName(p.Kind, false) + " (" + p.RT + ")   [" + p.Name + "]");
                        bool lsn = Math.Sqrt((double)p.LX * p.LX + (double)p.LY * p.LY) > 7849, lso = Math.Sqrt((double)o.LX * o.LX + (double)o.LY * o.LY) > 7849;
                        bool rsn = Math.Sqrt((double)p.RX * p.RX + (double)p.RY * p.RY) > 8689, rso = Math.Sqrt((double)o.RX * o.RX + (double)o.RY * o.RY) > 8689;
                        if (lsn != lso) W((lsn ? "stick izquierdo movido " : "stick izquierdo suelto ") + p.LX + ", " + p.LY + "   [" + p.Name + "]");
                        if (rsn != rso) W((rsn ? "stick derecho movido " : "stick derecho suelto ") + p.RX + ", " + p.RY + "   [" + p.Name + "]");
                    }
                    foreach (string k in last.Keys) if (!now.ContainsKey(k)) W("desconectado: " + last[k].Describe());
                    last = now;
                }
                TimeSpan cpu1 = me.TotalProcessorTime;
                double wall = loop.Elapsed.TotalSeconds;
                double ms = (cpu1 - cpu0).TotalMilliseconds;
                W("CPU del proceso durante la prueba (con el bucle de la prueba y el modo PC): " + ms.ToString("0", CultureInfo.InvariantCulture) + " ms en " +
                  wall.ToString("0.0", CultureInfo.InvariantCulture) + " s = " + (ms / 10 / wall).ToString("0.00", CultureInfo.InvariantCulture) + " % de un nucleo (" +
                  (ms / 10 / wall / Environment.ProcessorCount).ToString("0.000", CultureInfo.InvariantCulture) + " % del PC, " + Environment.ProcessorCount + " nucleos); " + polls + " lecturas");
                foreach (Pad p in hub.Pads()) W("al final: " + p.Describe() + ", " + p.Reports + " informes");
            }
            catch (Exception ex) { W("ERROR: " + ex); }
            finally
            {
                Log.Hook = null;
                try { File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false)); } catch { }
            }
            return 0;
        }
    }
}
