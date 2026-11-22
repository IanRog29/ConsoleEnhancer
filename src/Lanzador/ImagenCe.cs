// ImagenCe.cs - HDR, RTX HDR y VRR en ConsoleEnhancer (20-11-2026, agente CE-FUNCIONES)
//
// El usuario: "en el console enhancer añade la posibilidad de añadir rtx hdr (si se tiene nvidia claro), vrr etc."
//  - RTX HDR (rtx_hdr=1; NVIDIA RTX 20+, driver 551.52+, Windows 11 22H2+, pantalla HDR): el RTX HDR "de juegos" del driver, por programa,
//    como lo ponen la NVIDIA App / NVIDIA Profile Inspector / NvTrueHDR: ajustes del perfil de NVIDIA (DRS) de los exe que presentan la
//    imagen (ConsoleEnhancerEngine.exe, Magpie.exe, mpv.exe), puestos ANTES de abrir esos procesos (el driver los lee al arrancarlos) y
//    QUITADOS al salir (y al abrir, si una sesion anterior se corto). Ids (NVIDIA Profile Inspector, CustomSettingNames.xml / Reference.xml
//    y lo que escribe NvTrueHDR; comprobado el 20-11 por el agente de busqueda):
//      0x00DD48FB RTX HDR - Enable (1)            0x00DD48FC Peak Brightness (nits)      0x00DD48FD Middle Grey (nits)
//      0x00DD48FE Contrast (100 = neutro)         0x00DD48FF Saturation (100 = neutro)
//      0x00980896 Game Filters Enable (1)         0x1077A11A RTX HDR Allow (1)           0x00432F84 Driver Flags
//    Driver Flags: 0 = lo aplica el overlay de la NVIDIA App (necesita la App con su overlay; usa brillo / gris / contraste / saturacion);
//    != 0 = lo aplica el driver solo, sin la App, pero sin esos ajustes (pico ~1000 nits): 0x02 calidad muy alta (lo de NvTrueHDR),
//    0x0A media, 0x06 sin quitar bandas. rtx_hdr_calidad=alta (0x02, de serie) | media | sin_debanding | app (0). Se escribe con el
//    NvAPI_DRS_SetSettingEx privado (el de Profile Inspector) y, si no, el publico. MEDIDO 20-11 (driver del usuario): Allow y Driver Flags
//    los rechaza sin administrador (NVAPI -137); los demas no. Esos dos se ponen UNA vez con un aviso de UAC (al guardar el asistente con RTX
//    HDR, o al abrir la app si faltan; --ce-rtx-hdr-admin=<flags>) y se quedan; los demas se ponen al abrir y se devuelven al salir a lo que
//    habia (si un ajuste ya estaba en el perfil, su valor de antes; si no, se quita: data\picture_restore.txt, lineas drs=). Solo se engancha si la ventana es opaca y cubre la
//    pantalla entera (el motor si; Magpie y mpv depende): la app mira a los ~20-60 s si el proceso cargo nvAIHDR*.dll y lo apunta.
//    rtx_hdr_metodo=magpie: el efecto RTX HDR de Magpie (lo de Prisma, rtx_hdr de Config: solo con mpv + Magpie, ~15 ms a 4K).
//    Con RTX HDR la NVIDIA App pide el Auto HDR de Windows apagado: si esta encendido en global, se avisa en el log.
//  - HDR de Windows (hdr_windows=auto|0|1): RTX HDR lo necesita; si esta apagado, la app lo enciende (DisplayConfig de la pantalla de la
//    imagen; Windows 11 24H2+: SET_HDR_STATE y se lee con GET_ADVANCED_COLOR_INFO_2 / activeColorMode, antes SET_ADVANCED_COLOR_STATE) y
//    lo apaga al salir. 1 = siempre encendido con la app abierta; 0 = no tocarlo nunca. Sin administrador.
//  - Auto HDR de Windows: NO se pone (la preferencia por programa usa valores sin documentar y solo vale para los juegos de la lista de
//    Microsoft; para la app haria falta un truco de D3DBehaviors): no es limpio. En AMD / Intel no hay HDR añadido.
//  - VRR (vrr=auto|0|1): auto = encendido solo con la pantalla VRR, tearing de DXGI y, en NVIDIA, G-SYNC VISTO funcionando en esa pantalla
//    (los bits "activo / pedido / en modo VRR" de NvAPI_Disp_GetVRRInfo cambian con lo que este abierto: lo visto se recuerda en
//    data\vrr_seen.txt) y sin G-SYNC apagado en el perfil global (0x1094F1F7 = 0). Encendido: el motor recibe vrr=1 (y vrr_fg_vsync=
//    si motor_vrr_fg_vsync >= 0; LATENCIA4 de Prisma: el motor_vrr de Prisma = Config.EngineVrr lo decide esto, solo con la pantalla VRR
//    y tearing de DXGI: sin NVIDIA el motor no sabe si hay VRR y saldrian cortes), Magpie su VRR en los modos con DLSS FG (magpie_vrr_60_fg / _30_fg de Prisma, por DXGI con la
//    sincronizacion forzada: sin cortes) y, en NVIDIA, G-SYNC tambien en ventana para los tres exe (perfil: 0x1194F158 VRR_MODE = 2 y
//    0x1094F1F7 VRRREQUESTSTATE = 2, quitado al salir). Apagado: los tres magpie_vrr* a 0.
// Lo que se cambio en el sistema se apunta en data\picture_restore.txt hasta devolverlo.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public bool CeRtxHdr = false;            // rtx_hdr
        public string CeRtxHdrMetodo = "driver"; // rtx_hdr_metodo: driver | magpie
        public string CeRtxHdrCalidad = "alta";  // rtx_hdr_calidad: alta | media | sin_debanding | app
        public bool CeRtxHdrNitsAuto = true;     // rtx_hdr_nits=auto (o un numero: Config.RtxHdrNits de Prisma)
        public string CeHdrWindows = "auto";     // hdr_windows: auto | 0 | 1
        public string CeVrr = "auto";            // vrr: auto | 0 | 1
        public bool CeRtxHdrPosible, CeVrrPosible;   // *_posible: lo dice el plan (para la bandeja)

        bool CeParseImagen(string key, string val)
        {
            string v = val.Trim().ToLowerInvariant();
            bool si = v == "1" || v == "si" || v == "true" || v == "on" || v == "yes";
            switch (key)
            {
                case "rtx_hdr": CeRtxHdr = si; return true;
                case "rtx_hdr_metodo": CeRtxHdrMetodo = v == "magpie" ? "magpie" : "driver"; return true;
                case "rtx_hdr_calidad": CeRtxHdrCalidad = v == "media" || v == "sin_debanding" || v == "app" ? v : "alta"; return true;
                case "rtx_hdr_nits":
                    {
                        int n;
                        if (v == "auto" || v == "" || !int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { CeRtxHdrNitsAuto = true; return true; }
                        CeRtxHdrNitsAuto = false; RtxHdrNits = Math.Max(400, Math.Min(2000, n)); return true;
                    }
                case "hdr_windows": CeHdrWindows = v == "1" || v == "0" ? v : "auto"; return true;
                case "vrr": CeVrr = v == "1" || v == "0" ? v : "auto"; return true;
                case "rtx_hdr_posible": CeRtxHdrPosible = si; return true;
                case "vrr_posible": CeVrrPosible = si; return true;
                case "auto_hdr": case "auto_hdr_posible": return true;   // (de una prueba del 20-11 que se quito: Auto HDR no se pone)
                // (fusion G, LATENCIA5 de Prisma) auto | 0 | 1; la de Prisma (1 de serie) era 0|1: aqui va antes y la tapa
                case "motor_60fg_refresco_fijo": CeRefrescoFijo = v == "0" || v == "1" ? v : "auto"; EngineFixed60Fg = CeRefrescoFijo != "0"; return true;
            }
            return false;
        }

        // ---- (fusion G, 20-11) motor_60fg_refresco_fijo=auto|0|1 (de serie auto). En el modo 60 con DLSS FG del motor propio, el refresco fijo
        // (G-SYNC apagado SOLO en el perfil de NVIDIA de ConsoleEnhancerEngine.exe, NvProfiles.SetEngineFixed de Prisma) quita el tope de Reflex
        // con G-SYNC (~hz - hz^2/3600: 116 a 120 Hz, 138 a 144 Hz): medido en Prisma (RTX 4060 Ti, tele de 120 Hz) 59,9 reales/s en vez de 57,8
        // y 25 ms de la llegada a verse en vez de 32,6, sin cortes. auto = solo si: NVIDIA; VRR en uso con el motor (Config.EngineVrr, lo que
        // decide vrr=, ImagenCe.AlAbrir); el tope no deja pasar el doble de las imagenes de la captura (tope < 2 x fps, p. ej. 116 < 119,88);
        // y la pantalla va mas rapida que esa salida (a 119,88 Hz no: con el refresco fijo la cola se llena, medido en Prisma). 1 = siempre en
        // el 60 + DLSS FG (con NVIDIA), 0 = nunca. Lo demas (Smooth Motion, el 30, sin generacion) sigue con VRR, como en Prisma
        public string CeRefrescoFijo = "auto";
        static int ceFijoHz = -1; static DateTime ceFijoHzAt; static string ceFijoDicho = "";
        public bool CeRefrescoFijoAhora()
        {
            if (Ce.NoNvidia || CeRefrescoFijo == "0") return false;
            if (CeRefrescoFijo == "1") return true;
            DateTime now = DateTime.UtcNow;
            if (ceFijoHz < 0 || (now - ceFijoHzAt).TotalSeconds > 2) { ceFijoHz = Pantalla.Hz(); ceFijoHzAt = now; }
            double hz = ceFijoHz, fps = CeFps(Framerate), tope = hz - hz * hz / 3600.0;
            bool si = EngineVrr > 0 && hz >= 100 && fps > 0 && tope < 2 * fps && hz > 2 * fps;
            string porque = (si ? "si" : "no") + " (" + (EngineVrr > 0 ? "VRR del motor" : "sin VRR en el motor") + ", pantalla " + ceFijoHz + " Hz: tope de Reflex con G-SYNC ~" +
                            tope.ToString("0", CultureInfo.InvariantCulture) + " frente a 2 x " + fps.ToString("0.00", CultureInfo.InvariantCulture) +
                            (si ? ")" : hz > 0 && hz <= 2 * fps ? "; la pantalla no va mas rapida que la salida)" : ")");
            if (porque != ceFijoDicho) { ceFijoDicho = porque; Log.Write("Refresco fijo del motor en el 60 con DLSS FG (motor_60fg_refresco_fijo=auto): " + porque); }
            return si;
        }
        static double CeFps(string fr)
        {
            try
            {
                string[] p = (fr ?? "").Split('/');
                double n = double.Parse(p[0], CultureInfo.InvariantCulture), d = p.Length > 1 ? double.Parse(p[1], CultureInfo.InvariantCulture) : 1;
                return d > 0 ? n / d : 0;
            }
            catch { return 59.94; }
        }
    }

    static class CeImagen
    {
        public static volatile bool VrrAhora;      // vrr resuelto para esta sesion
        public static string VrrPorque = "";
        static PantallaInfo pantalla;              // la de la imagen (la principal), vista al abrir
        static readonly object sync = new object();
        static string Marca { get { return Path.Combine(Paths.Datos, "picture_restore.txt"); } }
        static string FicheroVrrVisto { get { return Path.Combine(Paths.Datos, "vrr_seen.txt"); } }
        public static readonly string[] Exes = { "ConsoleEnhancerEngine.exe", "Magpie.exe", "mpv.exe" };

        // ------------------------------------------------------------------ al abrir (Program.Main, tras leer el ini) y al salir
        // sistema=false (--dry-run): solo lo que se decide (VRR, escalas), sin tocar el HDR ni los perfiles
        public static void AlAbrir(Config cfg, bool sistema = true)
        {
            try { Escalas.Aplicar(cfg); } catch (Exception ex) { Log.Error("Escalas", ex); }
            try
            {
                if (sistema) Restaurar("de una sesion anterior que no se cerro bien");
                pantalla = PantallaPrincipal(!Ce.NoNvidia);
                // ---- VRR
                bool visto = VrrVisto(pantalla);
                int global = Ce.NoNvidia || cfg.CeVrr != "auto" || pantalla == null || pantalla.VrrNvidia < 0 || !pantalla.VrrPosible ? -1 : NvDrsCe.LeerGlobal(NvDrsCe.VrrRequestState);
                string porque;
                bool auto = PlanHdrVrr.VrrAuto(pantalla, !Ce.NoNvidia, visto, global, out porque);
                VrrAhora = cfg.CeVrr == "1" || (cfg.CeVrr == "auto" && auto);
                VrrPorque = cfg.CeVrr == "auto" ? "auto: " + porque : "vrr=" + cfg.CeVrr;
                if (VrrAhora) { cfg.MagpieVrr60Fg = true; cfg.MagpieVrr30Fg = true; }
                else { cfg.MagpieVrr = false; cfg.MagpieVrr60Fg = false; cfg.MagpieVrr30Fg = false; }
                // (fusion 20-11, LATENCIA4 de Prisma) Un solo VRR: el motor_vrr de Prisma (Config.EngineVrr, que pone vrr=1 y vrr_fg_vsync= en la
                // linea del motor) sale de lo que decide vrr= aqui; motor_vrr del ini no cuenta. Al motor solo si la pantalla dice tener VRR y
                // DXGI deja presentar sin sincronizar: con vrr=1 el motor presenta con ALLOW_TEARING y, sin NVIDIA (NVAPI), no puede saber si
                // la pantalla esta en VRR: en una sin VRR saldrian cortes. (Magpie si: su VRR va con la sincronizacion forzada, sin cortes)
                bool vrrMotor = VrrAhora && pantalla != null && pantalla.VrrPosible && pantalla.Tearing;
                if (cfg.EngineVrr > 0 && !vrrMotor) Log.Write("motor_vrr=1 del ini no cuenta: en ConsoleEnhancer lo decide vrr= (" + VrrPorque + ")");
                cfg.EngineVrr = vrrMotor ? 1 : 0;
                Log.Write("VRR " + (VrrAhora ? "encendido" : "apagado") + " (" + VrrPorque + "): motor vrr=" + (vrrMotor ? "1" + (cfg.EngineVrrFgVsync >= 0 ? " vrr_fg_vsync=" + cfg.EngineVrrFgVsync : "") : "0") +
                          (VrrAhora && !vrrMotor ? " (la pantalla no dice tener VRR o DXGI no deja presentar sin sincronizar: el motor sin VRR, para no dar cortes)" : "") +
                          ", Magpie en los modos con DLSS FG " + (VrrAhora ? "si" : "no"));
                // ---- HDR
                bool hdrPant = pantalla != null && (pantalla.HdrPosible || pantalla.HdrActivo);
                double nits = pantalla != null ? pantalla.NitsMax : 0;
                if (cfg.CeRtxHdrNitsAuto)
                {
                    int[] vals = PlanHdrVrr.RtxHdrValores(nits, sistema ? HdrWindows.BlancoSdr() : 0);
                    cfg.RtxHdrNits = vals[0]; cfg.RtxHdrGray = vals[1];
                }
                bool rtx = cfg.CeRtxHdr && !Ce.NoNvidia && hdrPant;
                bool magpie = rtx && cfg.CeRtxHdrMetodo == "magpie";
                cfg.RtxHdr = magpie;   // (el efecto de Magpie de Prisma solo con rtx_hdr_metodo=magpie; si no, el del driver)
                bool driver = rtx && !magpie;
                if (cfg.CeRtxHdr && !rtx) Log.Write("RTX HDR pedido (rtx_hdr=1) pero " + (Ce.NoNvidia ? "sin NVIDIA" : "la pantalla no tiene HDR") + ": no se pone");
                // mpv (imagen directa: HOME / menus a 4K nativo) con salida SDR de 8 bits, la que el RTX HDR del driver convierte
                if (driver) cfg.TestMpvArgs = (cfg.TestMpvArgs + " --target-colorspace-hint=no --d3d11-output-format=rgba8 --d3d11-output-csp=srgb").Trim();
                if (!sistema)
                {
                    Log.Write("Imagen (prueba, sin tocar el sistema): RTX HDR " + (driver ? "del driver (" + cfg.CeRtxHdrCalidad + ")" : magpie ? "de Magpie" : "no") + ", pico " + cfg.RtxHdrNits + " nits, gris " + cfg.RtxHdrGray);
                    Log.Write("Motor (argumentos de ConsoleEnhancer): " + string.Join(" ", Ce.EngineArgsExtra(cfg)));
                    return;
                }
                bool quiereHdr = cfg.CeHdrWindows == "1" || (cfg.CeHdrWindows == "auto" && rtx);
                List<string> marca = new List<string>();
                if (quiereHdr && hdrPant && pantalla != null && HdrWindows.Encendido() != 1)
                {
                    bool ok = HdrWindows.Poner(true);
                    Log.Write("HDR de Windows encendido para la imagen (" + pantalla.Nombre + "): " + (ok ? "hecho" : "NO SE PUDO") + " (" + HdrWindows.Ultimo + ")");
                    if (ok) marca.Add("hdr_windows=0");
                    // (el blanco SDR de Windows vale con el HDR encendido: el gris medio con el)
                    if (ok && cfg.CeRtxHdrNitsAuto) cfg.RtxHdrGray = PlanHdrVrr.RtxHdrValores(nits, HdrWindows.BlancoSdr())[1];
                }
                if (driver)
                {
                    if (AutoHdrGlobal()) Log.Write("Aviso: el Auto HDR de Windows esta encendido (Configuracion > Pantalla > HDR); RTX HDR pide apagarlo");
                    uint flags = cfg.CeRtxHdrCalidad == "app" ? 0u : cfg.CeRtxHdrCalidad == "media" ? 0x0Au : cfg.CeRtxHdrCalidad == "sin_debanding" ? 0x06u : 0x02u;
                    // Allow (0x1077A11A) y Driver Flags (0x00432F84) los rechaza el driver sin administrador (NVAPI -137, medido el 20-11): se
                    // ponen UNA vez con un aviso de UAC (ConsoleEnhancer.exe --ce-rtx-hdr-admin=<flags>) y se quedan (sin Enable no hacen nada).
                    // Sin ellos (UAC cancelado) va por el overlay de la NVIDIA App si la hay
                    NvDrsCe.AsegurarAdmin(Exes, flags);
                    List<string> hechos = NvDrsCe.PonerRtxHdr(Exes, cfg.RtxHdrNits, cfg.RtxHdrGray, cfg.RtxHdrContrast, cfg.RtxHdrSaturation);
                    Log.Write("RTX HDR del driver (" + (flags == 0 ? "por el overlay de la NVIDIA App" : "solo el driver, flags 0x" + flags.ToString("X2")) + "): " +
                              (hechos.Count > 0 ? string.Join(", ", hechos.ToArray()) : "no se pudo poner") + " (" + NvDrsCe.Ultimo + "; pico " + cfg.RtxHdrNits +
                              " nits, gris " + cfg.RtxHdrGray + ", contraste " + cfg.RtxHdrContrast + ", saturacion " + cfg.RtxHdrSaturation + ")");
                    if (hechos.Count > 0) VigilarRtxHdr();
                }
                else if (magpie) Log.Write("RTX HDR de Magpie (rtx_hdr_metodo=magpie): solo con mpv + Magpie, pico " + cfg.RtxHdrNits + " nits");
                if (VrrAhora && !Ce.NoNvidia)
                {
                    List<string> hechos = NvDrsCe.PonerVrrVentana(Exes);
                    Log.Write("G-SYNC en ventana para " + (hechos.Count > 0 ? string.Join(", ", hechos.ToArray()) : "ninguno") + " (" + NvDrsCe.Ultimo + ")");
                }
                lock (NvDrsCe.Antes) foreach (string a in NvDrsCe.Antes) marca.Add("drs=" + a);   // (lo de antes de cada ajuste de NVIDIA: se devuelve tal cual)
                if (marca.Count > 0) EscribirMarca(marca);
                VigilarVrr();
            }
            catch (Exception ex) { Log.Error("Imagen (HDR / VRR) al abrir", ex); }
        }

        public static void AlCerrar()
        {
            try { Restaurar("al salir"); } catch (Exception ex) { Log.Error("Imagen (HDR / VRR) al salir", ex); }
        }

        // Lo apuntado en la marca, de vuelta (HDR de Windows, RTX HDR y G-SYNC en ventana de los perfiles)
        static void Restaurar(string cuando)
        {
            bool fijo = false;
            try { RestaurarMarca(cuando, out fijo); }
            finally
            {
                // (fusion G) el refresco fijo del motor (Prisma LATENCIA5) que no se quito (la app se cayo): fuera ya del candado de la marca,
                // que SetEngineFixed vuelve a tocar (MarcaRefrescoFijo) con el suyo cogido
                if (fijo)
                {
                    bool ok = NvProfiles.SetEngineFixed(false);
                    Log.Write("Imagen: refresco fijo del perfil de ConsoleEnhancerEngine.exe " + (ok ? "quitado" : "NO SE PUDO QUITAR") + " (" + cuando + ")");
                    if (!ok) MarcaRefrescoFijo(true);
                }
            }
        }

        static void RestaurarMarca(string cuando, out bool fijo)
        {
            fijo = false;
            lock (sync)
            {
                if (!File.Exists(Marca)) return;
                string[] ls;
                try { ls = File.ReadAllLines(Marca); } catch { return; }
                List<string> rtx = new List<string>(), vrr = new List<string>(), drs = new List<string>();
                bool hdrOff = false;
                foreach (string l in ls)
                {
                    if (l.StartsWith("hdr_windows=0")) hdrOff = true;
                    else if (l.StartsWith("rtx_hdr_exe=")) rtx.Add(l.Substring(12));
                    else if (l.StartsWith("vrr_exe=")) vrr.Add(l.Substring(8));
                    else if (l.StartsWith("drs=")) drs.Add(l.Substring(4));
                    else if (l.StartsWith("fijo_exe=")) fijo = true;
                }
                List<string> r = new List<string>();
                if (drs.Count > 0) r.Add("perfiles de NVIDIA como estaban (" + NvDrsCe.Devolver(drs) + " ajustes)");
                lock (NvDrsCe.Antes) NvDrsCe.Antes.Clear();
                if (rtx.Count > 0) r.Add("RTX HDR quitado de " + NvDrsCe.Quitar(rtx.ToArray(), NvDrsCe.RtxHdrIds) + " perfil(es)");
                if (vrr.Count > 0) r.Add("G-SYNC en ventana quitado de " + NvDrsCe.Quitar(vrr.ToArray(), NvDrsCe.VrrIds) + " perfil(es)");
                if (hdrOff) { bool ok = HdrWindows.Poner(false); r.Add("HDR de Windows apagado" + (ok ? "" : " (NO SE PUDO: " + HdrWindows.Ultimo + ")")); }
                try { File.Delete(Marca); } catch { }
                Integrado.QuitarDeLaCopia(Path.GetFileName(Marca));   // (22-11, 1.2.2: si no, una version nueva la hereda de la copia, Integrado.cs)
                // (22-11, 1.2.2: con solo el refresco fijo del motor, la linea salia vacia: "devuelto ...: ")
                if (r.Count == 0) r.Add(fijo ? "el refresco fijo del motor (abajo)" : "nada (la marca estaba vacia)");
                Log.Write("Imagen: devuelto " + cuando + ": " + string.Join(", ", r.ToArray()));
            }
        }

        static void EscribirMarca(List<string> l)
        {
            try { Directory.CreateDirectory(Paths.Datos); File.WriteAllLines(Marca, l.ToArray(), new UTF8Encoding(false)); } catch (Exception ex) { Log.Error("Imagen: marca", ex); }
        }

        // (fusion G) NvProfiles.SetEngineFixed (Prisma LATENCIA5), cada vez que pone o quita el refresco fijo en el perfil del motor: la linea
        // fijo_exe= en la marca, junto a lo de HDR / RTX HDR / VRR (que no se toca: otros ajustes del mismo perfil). Si la app se cae con el
        // puesto, Restaurar lo quita al volver a abrirla
        public static void MarcaRefrescoFijo(bool puesto)
        {
            lock (sync)
            {
                try
                {
                    List<string> l = File.Exists(Marca) ? new List<string>(File.ReadAllLines(Marca)) : new List<string>();
                    l.RemoveAll(delegate(string x) { return x.StartsWith("fijo_exe="); });
                    if (puesto) l.Add("fijo_exe=ConsoleEnhancerEngine.exe");
                    if (l.Count > 0) EscribirMarca(l);
                    else if (File.Exists(Marca)) { File.Delete(Marca); Integrado.QuitarDeLaCopia(Path.GetFileName(Marca)); }
                }
                catch (Exception ex) { Log.Error("Imagen: marca del refresco fijo", ex); }
            }
        }

        static PantallaInfo PantallaPrincipal(bool nvidia)
        {
            OpcionesDeteccion op = new OpcionesDeteccion();
            op.UsarNvidia = nvidia;
            List<string> errs = new List<string>();
            List<PantallaInfo> l = DetectorPantallas.Detectar(op, errs);
            PantallaInfo p = l.Find(delegate(PantallaInfo x) { return x.Principal; });
            return p ?? (l.Count > 0 ? l[0] : null);
        }

        static bool AutoHdrGlobal()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                {
                    string s = k != null ? k.GetValue("DirectXUserGlobalSettings") as string : null;
                    return s != null && s.Replace(" ", "").IndexOf("AutoHDREnable=1", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        // Lo que el plan no sabe de la deteccion (asistente y --asistente-auto): G-SYNC visto y el modo global de G-SYNC (una vez por proceso)
        static int gsyncGlobal = -2;
        public static void Preferencias(PreferenciasPlan pref, PantallaInfo p)
        {
            try
            {
                pref.VrrVisto = VrrVisto(p);
                if (gsyncGlobal == -2) gsyncGlobal = Ce.NoNvidia || p == null || p.VrrNvidia < 0 ? -1 : NvDrsCe.LeerGlobal(NvDrsCe.VrrRequestState);
                pref.GsyncGlobal = gsyncGlobal;
            }
            catch { }
        }

        // ---- G-SYNC visto funcionando en una pantalla (se recuerda: los bits de NVAPI dependen de lo que este abierto)
        public static bool VrrVisto(PantallaInfo p)
        {
            if (p == null) return false;
            if (p.VrrNvidia >= 0 && (p.VrrNvidia & (1 | 4 | 16)) != 0) { RecordarVrr(p); return true; }
            try { return File.Exists(FicheroVrrVisto) && Array.IndexOf(File.ReadAllLines(FicheroVrrVisto), Clave(p)) >= 0; } catch { return false; }
        }

        static string Clave(PantallaInfo p) { return (p.Pnp.Length > 0 ? p.Pnp : p.Nombre) + " " + p.AnchoNativo + "x" + p.AltoNativo; }

        static void RecordarVrr(PantallaInfo p)
        {
            try
            {
                string c = Clave(p);
                List<string> l = File.Exists(FicheroVrrVisto) ? new List<string>(File.ReadAllLines(FicheroVrrVisto)) : new List<string>();
                if (l.Contains(c)) return;
                l.Add(c);
                Directory.CreateDirectory(Paths.Datos);
                File.WriteAllLines(FicheroVrrVisto, l.ToArray(), new UTF8Encoding(false));
                Log.Write("VRR: G-SYNC visto activo en " + c + " (se recuerda para vrr=auto)");
            }
            catch { }
        }

        // Mientras la app va con VRR (o sin el), si NVAPI dice que la pantalla esta en VRR, se recuerda (2 min, cada 20 s)
        static void VigilarVrr()
        {
            if (Ce.NoNvidia || pantalla == null || pantalla.Gdi.Length == 0) return;
            PantallaInfo p = pantalla;
            Thread t = new Thread(delegate ()
            {
                for (int i = 0; i < 6; i++)
                {
                    Thread.Sleep(20000);
                    try { int b = NvApi.Cargar(true) ? NvApi.Vrr(p.Gdi) : -1; if (b >= 0 && (b & (1 | 16)) != 0) { RecordarVrr(p); Log.Write("VRR: la pantalla esta en modo VRR (bits 0x" + b.ToString("X") + ")"); return; } }
                    catch { return; }
                }
            });
            t.IsBackground = true; t.Name = "VRR visto"; t.Start();
        }

        // ¿Se engancho RTX HDR? (el proceso carga nvAIHDR.dll / nvAIHDRx.dll): se mira a los 20, 40 y 60 s y se apunta una vez por exe
        static void VigilarRtxHdr()
        {
            Thread t = new Thread(delegate ()
            {
                HashSet<string> dicho = new HashSet<string>();
                for (int i = 0; i < 3; i++)
                {
                    Thread.Sleep(20000);
                    foreach (string exe in Exes)
                    {
                        if (dicho.Contains(exe)) continue;
                        foreach (Process pr in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                        {
                            try
                            {
                                bool si = false;
                                foreach (ProcessModule m in pr.Modules) if (m.ModuleName.StartsWith("nvAIHDR", StringComparison.OrdinalIgnoreCase)) { si = true; break; }
                                if (si || i == 2) { dicho.Add(exe); Log.Write("RTX HDR en " + exe + ": " + (si ? "enganchado (nvAIHDR cargada)" : "no enganchado (la ventana no es opaca a pantalla entera, o la pantalla no esta en HDR)")); }
                            }
                            catch { }
                            finally { pr.Dispose(); }
                        }
                    }
                }
            });
            t.IsBackground = true; t.Name = "RTX HDR visto"; t.Start();
        }

        // ------------------------------------------------------------------ al motor
        public static void ArgsMotor(Config cfg, List<string> a)
        {
            // (fusion 20-11) vrr=1 y vrr_fg_vsync= ya no van aqui: los pone EngineArgs de Switch2Magpie.cs con Config.EngineVrr, que decide
            // AlAbrir con vrr= (una sola vez en la linea del motor; antes de la fusion iba aqui)
        }

        // ------------------------------------------------------------------ el bloque del plan (asistente)
        public static string BloqueIni(PlanImagen p, OpcionesImagen o)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# ===== Imagen: HDR, RTX HDR, VRR y botones (asistente o menu de la bandeja > Imagen) =====");
            sb.AppendLine("#  HDR: " + (p.HdrPantalla ? "pantalla HDR" + (p.NitsPantalla > 0 ? " de " + Tx.N(p.NitsPantalla, "0") + " nits" : "") : "la pantalla no tiene HDR") +
                          "; RTX HDR " + (p.RtxHdrPosible ? "posible" : "no (" + p.RtxHdrMotivo + ")"));
            sb.AppendLine("#  VRR: " + (p.VrrPosible ? "posible" : "no") + (p.VrrMotivo.Length > 0 ? " (" + p.VrrMotivo + ")" : ""));
            sb.AppendLine("#  rtx_hdr_calidad: alta (el driver solo, sin la NVIDIA App; pico ~1000 nits) | media | sin_debanding | app (el overlay de la");
            sb.AppendLine("#  NVIDIA App, con brillo / gris / contraste / saturacion de aqui). rtx_hdr_metodo=magpie: el efecto de Magpie (solo mpv + Magpie)");
            sb.AppendLine("rtx_hdr_posible=" + (p.RtxHdrPosible ? "1" : "0"));
            sb.AppendLine("vrr_posible=" + (p.VrrPosible ? "1" : "0"));
            sb.AppendLine("rtx_hdr=" + (o != null && o.RtxHdr && p.RtxHdrPosible ? "1" : "0"));
            sb.AppendLine("rtx_hdr_calidad=alta");
            sb.AppendLine("rtx_hdr_nits=auto");
            sb.AppendLine("hdr_windows=auto");
            sb.AppendLine("vrr=" + (o != null ? o.Vrr : "auto"));
            sb.AppendLine("mando_botones=" + (o != null ? o.Botones : "ce"));
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ HDR de Windows (DisplayConfig, "advanced color")
    static class HdrWindows
    {
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, byte[] paths, ref uint numModes, byte[] modes, IntPtr topologyId);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(byte[] packet);
        [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(byte[] packet);
        public static string Ultimo = "";
        static bool W24H2 { get { return Nat.OsBuild() >= 26100; } }   // (OSVersion de .NET se queda en 6.2.9200 sin manifiesto: RtlGetVersion)

        // La ruta de la pantalla principal (la de la imagen): LUID e id del destino
        static bool Destino(out long luid, out uint id)
        {
            luid = 0; id = 0;
            uint np, nm;
            if (GetDisplayConfigBufferSizes(2, out np, out nm) != 0 || np == 0) return false;
            byte[] paths = new byte[np * 72], modes = new byte[nm * 64];
            if (QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return false;
            for (int i = 0; i < np; i++)
            {
                int o = i * 72;
                uint srcMode = BitConverter.ToUInt32(paths, o + 12);
                bool principal = false;
                if (srcMode != 0xFFFFFFFF && srcMode < nm)
                {
                    int m = (int)srcMode * 64;
                    principal = BitConverter.ToInt32(modes, m) == 1 && BitConverter.ToInt32(modes, m + 28) == 0 && BitConverter.ToInt32(modes, m + 32) == 0;
                }
                if (!principal && np > 1) continue;
                luid = ((long)BitConverter.ToInt32(paths, o + 24) << 32) | BitConverter.ToUInt32(paths, o + 20);
                id = BitConverter.ToUInt32(paths, o + 28);
                return true;
            }
            return false;
        }

        static byte[] Paquete(int type, int size, long luid, uint id)
        {
            byte[] b = new byte[size];
            BitConverter.GetBytes(type).CopyTo(b, 0);
            BitConverter.GetBytes(size).CopyTo(b, 4);
            BitConverter.GetBytes((uint)(luid & 0xFFFFFFFF)).CopyTo(b, 8);
            BitConverter.GetBytes((int)(luid >> 32)).CopyTo(b, 12);
            BitConverter.GetBytes(id).CopyTo(b, 16);
            return b;
        }

        // ¿HDR encendido en la pantalla principal? (-1 no se sabe). 24H2+: GET_ADVANCED_COLOR_INFO_2 (15), activeColorMode == 2 (HDR; con la
        // gestion automatica del color "advanced color" esta activo tambien en SDR); antes GET_ADVANCED_COLOR_INFO (9), bit 1
        public static int Encendido()
        {
            long luid; uint id;
            if (!Destino(out luid, out id)) return -1;
            if (W24H2)
            {
                byte[] a2 = Paquete(15, 36, luid, id);
                if (DisplayConfigGetDeviceInfo(a2) == 0) return BitConverter.ToInt32(a2, 32) == 2 ? 1 : 0;
            }
            byte[] ac = Paquete(9, 32, luid, id);
            if (DisplayConfigGetDeviceInfo(ac) != 0) return -1;
            return (BitConverter.ToUInt32(ac, 20) & 2) != 0 ? 1 : 0;
        }

        // El blanco SDR de Windows en nits (GET_SDR_WHITE_LEVEL, 11: valor x 80 / 1000); 0 = no se sabe
        public static double BlancoSdr()
        {
            try
            {
                long luid; uint id;
                if (!Destino(out luid, out id)) return 0;
                byte[] w = Paquete(11, 24, luid, id);
                if (DisplayConfigGetDeviceInfo(w) != 0) return 0;
                return BitConverter.ToUInt32(w, 20) * 80.0 / 1000.0;
            }
            catch { return 0; }
        }

        // Enciende / apaga el HDR de Windows en la pantalla principal (como Configuracion > Pantalla > HDR). 24H2+: SET_HDR_STATE (16); antes
        // (o si falla): SET_ADVANCED_COLOR_STATE (10). Se comprueba despues (hasta 4 s: la tele cambia de modo)
        public static bool Poner(bool on)
        {
            try
            {
                long luid; uint id;
                if (!Destino(out luid, out id)) { Ultimo = "sin la ruta de la pantalla"; return false; }
                if (Encendido() == (on ? 1 : 0)) { Ultimo = "ya estaba " + (on ? "encendido" : "apagado"); return true; }
                int r = -1;
                Ultimo = "";
                if (W24H2)
                {
                    byte[] h = Paquete(16, 24, luid, id);
                    BitConverter.GetBytes(on ? 1u : 0u).CopyTo(h, 20);
                    r = DisplayConfigSetDeviceInfo(h);
                    Ultimo = "SET_HDR_STATE " + r;
                }
                if (r != 0)
                {
                    byte[] a = Paquete(10, 24, luid, id);
                    BitConverter.GetBytes(on ? 1u : 0u).CopyTo(a, 20);
                    r = DisplayConfigSetDeviceInfo(a);
                    Ultimo = (Ultimo.Length > 0 ? Ultimo + ", " : "") + "SET_ADVANCED_COLOR_STATE " + r;
                }
                if (r != 0) return false;
                for (int i = 0; i < 40; i++) { if (Encendido() == (on ? 1 : 0)) return true; Thread.Sleep(100); }
                Ultimo += ", sin confirmar en 4 s";
                return true;
            }
            catch (Exception ex) { Ultimo = ex.Message; return false; }
        }
    }

    // ------------------------------------------------------------------ perfiles de NVIDIA (DRS) de los exe de la app: RTX HDR y G-SYNC en ventana
    static class NvDrsCe
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface64(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnVoid();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnOutHandle(out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnHandle(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnProfileOut(IntPtr session, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnFindApp(IntPtr session, IntPtr appName, out IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnFindProfile(IntPtr session, IntPtr name, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnCreateProfile(IntPtr session, IntPtr info, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnCreateApp(IntPtr session, IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetSetting(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetSettingEx(IntPtr session, IntPtr profile, uint id, IntPtr setting, IntPtr notUsed);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnSetSetting(IntPtr session, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnSetSettingEx(IntPtr session, IntPtr profile, IntPtr setting, uint a, uint b);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnDelSetting(IntPtr session, IntPtr profile, uint id);

        // Ids de NVAPI (los de NvProfiles de Prisma; GetCurrentGlobalProfile de nvapi_interface.h)
        const uint IdInitialize = 0x0150E828, IdCreateSession = 0x0694D52E, IdDestroySession = 0xDAD9CFF8, IdLoadSettings = 0x375DBD6B,
                   IdSaveSettings = 0xFCBC7E14, IdFindApplicationByName = 0xEEE566B2, IdFindProfileByName = 0x7E4A9A0B, IdCreateProfile = 0xCC176068,
                   IdCreateApplication = 0x4347A9DE, IdGetSetting = 0x73BF8338, IdGetSettingEx = 0xEA99498D, IdSetSetting = 0x577DD202,
                   IdSetSettingEx = 0x8A2CF5F5, IdDeleteProfileSetting = 0xE4A26362, IdDeleteProfileSettingEx = 0xD20D29DF,
                   IdGetCurrentGlobalProfile = 0x617BFF9F;
        const int StringBytes = 4096, SettingSize = 12320, SettingIdOffset = 4100, SettingLocationOffset = 4108, SettingCurrentOffset = 8220,
                  ProfileSize = 4116, ProfileGpuSupportOffset = 4100, AppSize = 12296, AppNameOffset = 8;
        const int NvOk = 0, NvSettingNotFound = -160, NvProfileNotFound = -163, NvExecutableNotFound = -166;

        // RTX HDR (ver la cabecera del fichero)
        public const uint RtxEnable = 0x00DD48FB, RtxPeak = 0x00DD48FC, RtxGray = 0x00DD48FD, RtxContrast = 0x00DD48FE, RtxSaturation = 0x00DD48FF,
                          GameFilters = 0x00980896, RtxAllow = 0x1077A11A, RtxFlags = 0x00432F84;
        // Los que se ponen y quitan en cada sesion (sin administrador); Allow y Driver Flags piden administrador (-137) y se quedan
        public static readonly uint[] RtxHdrIds = { RtxEnable, RtxPeak, RtxGray, RtxContrast, RtxSaturation, GameFilters };
        const int NvInvalidUserPrivilege = -137;
        // G-SYNC (NvApiDriverSettings.h de NVIDIA/nvapi): VRR_MODE 0 apagado, 1 pantalla completa, 2 pantalla completa y ventana; VRRREQUESTSTATE igual
        public const uint VrrMode = 0x1194F158, VrrRequestState = 0x1094F1F7;
        public static readonly uint[] VrrIds = { VrrMode, VrrRequestState };
        public static string Ultimo = "";

        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = QueryInterface64(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("NVAPI sin la funcion 0x" + id.ToString("X8"));
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        static IntPtr Alloc(List<IntPtr> mem, int size)
        {
            IntPtr p = Marshal.AllocHGlobal(size);
            mem.Add(p);
            Marshal.Copy(new byte[size], 0, p, size);
            Marshal.WriteInt32(p, 0, size | (1 << 16));
            return p;
        }

        static IntPtr Str(List<IntPtr> mem, string s)
        {
            IntPtr p = Marshal.AllocHGlobal(StringBytes);
            mem.Add(p);
            Marshal.Copy(new byte[StringBytes], 0, p, StringBytes);
            byte[] b = Encoding.Unicode.GetBytes(s);
            Marshal.Copy(b, 0, p, b.Length);
            return p;
        }

        static void WriteString(IntPtr block, int offset, string s)
        {
            byte[] b = Encoding.Unicode.GetBytes(s);
            Marshal.Copy(b, 0, new IntPtr(block.ToInt64() + offset), b.Length);
        }

        // Una sesion de DRS: abrir, hacer, (guardar), cerrar. false = no se pudo abrir
        delegate void Trabajo(IntPtr session, List<IntPtr> mem);
        static bool Sesion(Trabajo t)
        {
            if (Ce.NoNvidia) { Ultimo = "sin NVIDIA"; return false; }
            List<IntPtr> mem = new List<IntPtr>();
            IntPtr session = IntPtr.Zero;
            try
            {
                if (Fn<FnVoid>(IdInitialize)() != NvOk) { Ultimo = "NvAPI_Initialize"; return false; }
                if (Fn<FnOutHandle>(IdCreateSession)(out session) != NvOk) { Ultimo = "CreateSession"; return false; }
                if (Fn<FnHandle>(IdLoadSettings)(session) != NvOk) { Ultimo = "LoadSettings"; return false; }
                t(session, mem);
                return true;
            }
            catch (Exception ex) { Ultimo = ex.GetType().Name + ": " + ex.Message; return false; }
            finally
            {
                if (session != IntPtr.Zero) { try { Fn<FnHandle>(IdDestroySession)(session); } catch { } }
                foreach (IntPtr p in mem) Marshal.FreeHGlobal(p);
            }
        }

        // El perfil del exe (el suyo si ya esta en uno; si no, "ConsoleEnhancer - <exe>", como NvProfiles)
        static IntPtr Perfil(IntPtr session, List<IntPtr> mem, string exe, bool crear)
        {
            IntPtr profile;
            int rc = Fn<FnFindApp>(IdFindApplicationByName)(session, Str(mem, exe), out profile, Alloc(mem, AppSize));
            if (rc == NvOk) return profile;
            if (rc != NvExecutableNotFound || !crear) return IntPtr.Zero;
            string name = "ConsoleEnhancer - " + exe;
            rc = Fn<FnFindProfile>(IdFindProfileByName)(session, Str(mem, name), out profile);
            if (rc == NvProfileNotFound)
            {
                IntPtr info = Alloc(mem, ProfileSize);
                WriteString(info, 4, name);
                Marshal.WriteInt32(info, ProfileGpuSupportOffset, 1);
                if (Fn<FnCreateProfile>(IdCreateProfile)(session, info, out profile) != NvOk) return IntPtr.Zero;
            }
            else if (rc != NvOk) return IntPtr.Zero;
            IntPtr app = Alloc(mem, AppSize);
            WriteString(app, AppNameOffset, exe);
            if (Fn<FnCreateApp>(IdCreateApplication)(session, profile, app) != NvOk) return IntPtr.Zero;
            return profile;
        }

        // El privado (Ex, el de Profile Inspector: tambien los ajustes ocultos) y, si no, el publico
        static int Set(IntPtr session, IntPtr profile, List<IntPtr> mem, uint id, uint value)
        {
            IntPtr w = Alloc(mem, SettingSize);
            Marshal.WriteInt32(w, SettingIdOffset, unchecked((int)id));
            Marshal.WriteInt32(w, SettingCurrentOffset, unchecked((int)value));
            int rc;
            try { rc = Fn<FnSetSettingEx>(IdSetSettingEx)(session, profile, w, 0, 0); }
            catch (InvalidOperationException) { rc = NvSettingNotFound; }
            if (rc != NvOk) rc = Fn<FnSetSetting>(IdSetSetting)(session, profile, w);
            return rc;
        }

        // Pone una lista de ajustes {id, valor} en el perfil de cada exe y guarda; devuelve los exe hechos
        // Lo que habia antes de cada ajuste puesto, para devolverlo al salir: "exe|id|valor" (valor -1 = no estaba en el perfil: se quita)
        public static readonly List<string> Antes = new List<string>();

        static List<string> Poner(string[] exes, uint[][] ajustes, uint obligatorio)
        {
            List<string> hechos = new List<string>();
            List<string> malos = new List<string>();
            bool ok = Sesion(delegate(IntPtr session, List<IntPtr> mem)
            {
                foreach (string exe in exes)
                {
                    IntPtr prof = Perfil(session, mem, exe, true);
                    if (prof == IntPtr.Zero) { malos.Add(exe + ": sin perfil"); continue; }
                    bool bien = true;
                    foreach (uint[] a in ajustes)
                    {
                        long prev = Propio(session, mem, prof, a[0]);
                        if (prev == a[1]) continue;   // (ya estaba asi: ni se pone ni se toca al salir)
                        int rc = Set(session, prof, mem, a[0], a[1]);
                        if (rc != NvOk) { malos.Add(exe + ": 0x" + a[0].ToString("X8") + " " + rc); if (a[0] == obligatorio) bien = false; continue; }
                        lock (Antes) Antes.Add(exe + "|" + a[0].ToString("X8") + "|" + prev.ToString(CultureInfo.InvariantCulture));
                    }
                    if (bien) hechos.Add(exe);
                }
                int sv = Fn<FnHandle>(IdSaveSettings)(session);
                if (sv != NvOk) { malos.Add("SaveSettings " + sv + " (¿hace falta administrador?)"); hechos.Clear(); lock (Antes) Antes.Clear(); }
            });
            if (ok) Ultimo = malos.Count > 0 ? string.Join("; ", malos.ToArray()) : "ok";
            return hechos;
        }

        // Devuelve cada ajuste a lo de antes ("exe|id|valor": -1 = quitarlo del perfil); devuelve cuantos
        public static int Devolver(List<string> lineas)
        {
            int n = 0;
            Sesion(delegate(IntPtr session, List<IntPtr> mem)
            {
                foreach (string l in lineas)
                {
                    string[] c = l.Split('|');
                    uint id; long prev;
                    if (c.Length != 3 || !uint.TryParse(c[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id) ||
                        !long.TryParse(c[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out prev)) continue;
                    IntPtr prof = Perfil(session, mem, c[0], false);
                    if (prof == IntPtr.Zero) continue;
                    int rc;
                    if (prev < 0)
                    {
                        rc = Fn<FnDelSetting>(IdDeleteProfileSettingEx)(session, prof, id);
                        if (rc != NvOk) rc = Fn<FnDelSetting>(IdDeleteProfileSetting)(session, prof, id);
                    }
                    else rc = Set(session, prof, mem, id, (uint)prev);
                    if (rc == NvOk) n++;
                }
                if (n > 0) Fn<FnHandle>(IdSaveSettings)(session);
            });
            return n;
        }

        public static List<string> PonerRtxHdr(string[] exes, int nits, int gris, int contraste, int saturacion)
        {
            return Poner(exes, new uint[][]
            {
                new[] { GameFilters, 1u },
                new[] { RtxPeak, (uint)nits }, new[] { RtxGray, (uint)gris }, new[] { RtxContrast, (uint)contraste }, new[] { RtxSaturation, (uint)saturacion },
                new[] { RtxEnable, 1u },
            }, RtxEnable);
        }

        // El valor propio de un ajuste en el perfil de un exe (-1 = no esta puesto en el o no se sabe)
        public static long Leer(string exe, uint id)
        {
            long v = -1;
            Sesion(delegate(IntPtr session, List<IntPtr> mem)
            {
                IntPtr prof = Perfil(session, mem, exe, false);
                if (prof != IntPtr.Zero) v = Propio(session, mem, prof, id);
            });
            return v;
        }

        static long Propio(IntPtr session, List<IntPtr> mem, IntPtr prof, uint id)
        {
            IntPtr s = Alloc(mem, SettingSize);
            int rc;
            try { rc = Fn<FnGetSettingEx>(IdGetSettingEx)(session, prof, id, s, Alloc(mem, 64)); }
            catch (InvalidOperationException) { rc = NvSettingNotFound; }
            if (rc != NvOk) rc = Fn<FnGetSetting>(IdGetSetting)(session, prof, id, s);
            return rc == NvOk && Marshal.ReadInt32(s, SettingLocationOffset) == 0 ? (long)(uint)Marshal.ReadInt32(s, SettingCurrentOffset) : -1L;
        }

        // Allow = 1 y Driver Flags = flags en el perfil de cada exe; si falta y no hay permiso, una vez por sesion con UAC (un exe elevado que
        // solo hace esto: Ce.Arranque, --ce-rtx-hdr-admin=<flags>). true = ya estan
        static bool adminPedido;
        public static bool AsegurarAdmin(string[] exes, uint flags)
        {
            List<string> faltan = new List<string>();
            // (con el overlay de la NVIDIA App, flags 0 = lo de serie: nada que pedir salvo que otra vez se pusieran otros)
            // (una sola sesion de DRS: cada una carga todos los perfiles, ~0,3-0,5 s)
            Sesion(delegate(IntPtr session, List<IntPtr> mem)
            {
                foreach (string e in exes)
                {
                    IntPtr prof = Perfil(session, mem, e, false);
                    long f = prof == IntPtr.Zero ? -1 : Propio(session, mem, prof, RtxFlags), a = prof == IntPtr.Zero ? -1 : Propio(session, mem, prof, RtxAllow);
                    if (flags == 0 ? f > 0 : a != 1 || f != flags) faltan.Add(e);
                }
            });
            if (faltan.Count == 0) return true;
            int r = PonerAdmin(faltan.ToArray(), flags);
            if (r == NvOk) { Log.Write("RTX HDR: Allow y Driver Flags 0x" + flags.ToString("X2") + " puestos en " + string.Join(", ", faltan.ToArray())); return true; }
            if (r != NvInvalidUserPrivilege || adminPedido) { Log.Write("RTX HDR: Allow / Driver Flags no se pudieron poner (NVAPI " + r + ")"); return false; }
            adminPedido = true;
            try
            {
                Log.Write("RTX HDR: Allow y Driver Flags piden administrador: se pide permiso (una vez; se quedan en los perfiles)");
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--ce-rtx-hdr-admin=" + flags.ToString(CultureInfo.InvariantCulture));
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(30000)) return false;
                    bool ok = p.ExitCode == 0;
                    Log.Write("RTX HDR: Allow / Driver Flags con administrador: " + (ok ? "hecho" : "fallo (" + p.ExitCode + ")"));
                    return ok;
                }
            }
            catch (Exception ex) { Log.Write("RTX HDR: sin permiso de administrador (" + ex.Message + "): sin Driver Flags va por el overlay de la NVIDIA App, si esta"); return false; }
        }

        // (en el exe elevado o si ya se tiene permiso) devuelve el codigo de NVAPI del primero que falle (0 = todo bien)
        public static int PonerAdmin(string[] exes, uint flags)
        {
            int peor = NvOk;
            bool ok = Sesion(delegate(IntPtr session, List<IntPtr> mem)
            {
                foreach (string exe in exes)
                {
                    IntPtr prof = Perfil(session, mem, exe, true);
                    if (prof == IntPtr.Zero) { peor = -1; continue; }
                    int a = Set(session, prof, mem, RtxAllow, 1u), b = Set(session, prof, mem, RtxFlags, flags);
                    if (a != NvOk && peor == NvOk) peor = a;
                    if (b != NvOk && peor == NvOk) peor = b;
                }
                int sv = Fn<FnHandle>(IdSaveSettings)(session);
                if (sv != NvOk && peor == NvOk) peor = sv;
            });
            return ok ? peor : -1;
        }

        public static List<string> PonerVrrVentana(string[] exes)
        {
            return Poner(exes, new uint[][] { new[] { VrrMode, 2u }, new[] { VrrRequestState, 2u } }, VrrMode);
        }

        // Quita esos ajustes del perfil de cada exe (vuelven a lo heredado); devuelve en cuantos perfiles habia algo
        public static int Quitar(string[] exes, uint[] ids)
        {
            int n = 0;
            Sesion(delegate(IntPtr session, List<IntPtr> mem)
            {
                foreach (string exe in exes)
                {
                    IntPtr prof = Perfil(session, mem, exe, false);
                    if (prof == IntPtr.Zero) continue;
                    bool algo = false;
                    foreach (uint id in ids)
                    {
                        int rc = Fn<FnDelSetting>(IdDeleteProfileSettingEx)(session, prof, id);
                        if (rc != NvOk) rc = Fn<FnDelSetting>(IdDeleteProfileSetting)(session, prof, id);
                        if (rc == NvOk) algo = true;
                    }
                    if (algo) n++;
                }
                if (n > 0) Fn<FnHandle>(IdSaveSettings)(session);
            });
            return n;
        }

        // Un ajuste del perfil global (el valor que vale, propio o de serie); -1 = no se sabe
        public static int LeerGlobal(uint id)
        {
            int v = -1;
            Sesion(delegate(IntPtr session, List<IntPtr> mem)
            {
                IntPtr prof;
                if (Fn<FnProfileOut>(IdGetCurrentGlobalProfile)(session, out prof) != NvOk) return;
                IntPtr s = Alloc(mem, SettingSize);
                int rc = Fn<FnGetSetting>(IdGetSetting)(session, prof, id, s);
                if (rc == NvSettingNotFound) rc = Fn<FnGetSettingEx>(IdGetSettingEx)(session, prof, id, s, Alloc(mem, 64));
                if (rc == NvOk) v = Marshal.ReadInt32(s, SettingCurrentOffset);
            });
            return v;
        }
    }

    // ------------------------------------------------------------------ menu de la bandeja > Imagen
    partial class TrayApp
    {
        // (una linea en MenuCe, src\InicioWindows.cs)
        void MenuCeImagen(ContextMenuStrip menu)
        {
            ToolStripMenuItem img = new ToolStripMenuItem(Idioma.T("Imagen (HDR, VRR, botones, escalas)"));
            ToolStripMenuItem rtx = new ToolStripMenuItem(Idioma.T("RTX HDR (reabre la app)"), null, delegate { CeCambiar("rtx_hdr", cfg.CeRtxHdr ? "0" : "1", true); });
            ToolStripMenuItem vrr = new ToolStripMenuItem("VRR");
            ToolStripMenuItem vA = new ToolStripMenuItem(Idioma.T("Automático (reabre la app)"), null, delegate { CeCambiar("vrr", "auto", true); });
            ToolStripMenuItem v1 = new ToolStripMenuItem(Idioma.T("Encendido (reabre la app)"), null, delegate { CeCambiar("vrr", "1", true); });
            ToolStripMenuItem v0 = new ToolStripMenuItem(Idioma.T("Apagado (reabre la app)"), null, delegate { CeCambiar("vrr", "0", true); });
            vrr.DropDownItems.AddRange(new ToolStripItem[] { vA, v1, v0 });
            ToolStripMenuItem bot = new ToolStripMenuItem(Idioma.T("Botones: Menú = modo 30/60, Vista = generación"), null, delegate
            {
                BotonesCe.Prisma = !BotonesCe.Prisma;
                Ce.IniPoner("mando_botones", BotonesCe.Prisma ? "prisma" : "ce");
                Log.Write("Botones del mando desde la bandeja: " + (BotonesCe.Prisma ? "los de Prisma (Vista = modo 30, Menu = modo 60)" : "Menu = modo 30/60, Vista = generacion"));
            });
            ToolStripMenuItem esc = new ToolStripMenuItem(Idioma.T("Escalas que se adaptan solas (reabre la app)"), null, delegate { CeCambiar("escalas_adaptables", cfg.CeEscalas ? "0" : "1", true); });
            ToolStripMenuItem olv = new ToolStripMenuItem(Idioma.T("Olvidar las escalas aprendidas (reabre la app)"), null, delegate
            {
                foreach (string f in new[] { "scales_engine.txt", "scales_magpie.txt" })
                    try { string r = Path.Combine(Paths.Datos, f); if (File.Exists(r)) File.Delete(r); } catch { }
                Log.Write("Escalas aprendidas borradas desde la bandeja");
                CeReabrir();
            });
            img.DropDownItems.AddRange(new ToolStripItem[] { rtx, vrr, new ToolStripSeparator(), bot, new ToolStripSeparator(), esc, olv });
            menu.Items.Add(img);
            CeSalidaRemota();   // (ConsoleEnhancer.exe --salir, pruebas)
            Action marcar = delegate
            {
                rtx.Checked = cfg.CeRtxHdr; rtx.Visible = cfg.CeRtxHdrPosible || cfg.CeRtxHdr;
                vA.Checked = cfg.CeVrr == "auto"; v1.Checked = cfg.CeVrr == "1"; v0.Checked = cfg.CeVrr == "0";
                vrr.Text = "VRR · " + (CeImagen.VrrAhora ? Idioma.T("encendido ahora") : Idioma.T("apagado ahora"));
                vrr.Enabled = cfg.CeVrrPosible || cfg.CeVrr != "auto";
                bot.Checked = !BotonesCe.Prisma;
                esc.Checked = cfg.CeEscalas;
            };
            marcar();
            menu.Opening += delegate { marcar(); };
        }

        // ConsoleEnhancer.exe --salir (pruebas): esta app se cierra como con Salir (y devuelve el HDR, RTX HDR y G-SYNC en ventana)
        bool ceSalidaPuesta;
        void CeSalidaRemota()
        {
            if (ceSalidaPuesta) return;
            ceSalidaPuesta = true;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    using (EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ConsoleEnhancer_Salir"))
                    {
                        ev.WaitOne();
                        Log.Write("--salir: la app se cierra (como Salir de la bandeja)");
                        OnUi(QuitRequested);
                    }
                }
                catch (Exception ex) { Log.Error("--salir", ex); }
            });
            t.IsBackground = true; t.Name = "--salir"; t.Start();
        }

        // Cambia una clave del ini; con reabrir, la app se vuelve a abrir con ella (RTX HDR, VRR y las escalas se ponen al arrancar los procesos)
        void CeCambiar(string clave, string valor, bool reabrir)
        {
            Ce.IniPoner(clave, valor);
            Log.Write("Bandeja: " + clave + "=" + valor + (reabrir ? " (se reabre la app)" : ""));
            if (reabrir) CeReabrir();
        }

        void CeReabrir()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(exe, "--esperar-salida");
                psi.UseShellExecute = false;
                System.Diagnostics.Process.Start(psi);
                QuitRequested();
            }
            catch (Exception ex) { Log.Error("Reabriendo la app", ex); }
        }
    }
}
