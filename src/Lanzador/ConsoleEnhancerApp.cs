// ConsoleEnhancerApp.cs - lo propio de ConsoleEnhancer en el lanzador (fase 2, agente UNIVERSAL, 18-11-2026)
//
// ConsoleEnhancer = Switch2Magpie + Switch2Engine + Magpie + mpv para cualquier consola, capturadora y PC (C:\ConsoleEnhancer\DISENO.md).
// Aqui:
//  - Claves nuevas del ini (Config es partial): device_path, audio_dispositivo, audio_pines, gpu_fabricante, motor_streamline,
//    motor_adaptador, generacion_disponible, idioma
//  - Ce.Arranque: antes de leer el ini. Sin ini (primer arranque), con --asistente o si el hardware ha cambiado (huella en
//    data\hardware.txt): la deteccion (src\Deteccion) y el asistente (Asistente.cs). --asistente-auto (y --dry-run sin ini): el plan
//    de la deteccion sin preguntar nada
//  - Ce.EscribirIni: resources\base.ini (ajustes generales probados) + el bloque del plan (lo que depende del hardware) -> el ini
//  - Lo que el resto del lanzador pregunta: si hay NVIDIA (NvProfiles), el escalador y los argumentos nuevos del motor, si la
//    generacion es solo de Magpie (FSR 3 FG / XeSS FG: entonces mpv + Magpie con ella encendida)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public string DevicePath = "";           // device_path: DevicePath de DirectShow (el motor abre ese dispositivo exacto)
        public string AudioDevice = "";          // audio_dispositivo: el dispositivo de audio de DirectShow ("" = el de video, device=)
        public bool AudioPins = true;            // audio_pines: el audio sale por pines del filtro de video (audio_pin_name, como la GC573)
        public string GpuVendor = "";            // gpu_fabricante: nvidia | amd | intel | otro ("" = sin detectar: como NVIDIA)
        public string EngineStreamline = "auto"; // motor_streamline: auto | 0 | 1
        public string EngineAdapter = "";        // motor_adaptador: LUID de la grafica (hex)
        public bool FgAvailable = true;          // generacion_disponible: 0 = esta grafica no tiene generacion en la app (p. ej. AFMF del driver)
        public string Language = "auto";         // idioma: auto | es | en

        // Claves de ConsoleEnhancer (Config.Load las prueba antes que las de siempre)
        public bool CeParse(string key, string val)
        {
            if (CeParseSuspension(key, val)) return true; // (apagar_pc_modo: src\Suspension.cs)
            if (CeParseInicio(key, val)) return true; // (iniciar_con_windows, apagar_pc_aviso_s: src\InicioWindows.cs)
            if (BotonesCe.Parse(key, val)) return true; // (20-11: mando_botones, src\BotonesCe.cs)
            if (CeParseImagen(key, val)) return true;   // (20-11: hdr, rtx_hdr*, vrr, nvidia_latencia..., src\ImagenCe.cs)
            if (CeParseEscalas(key, val)) return true;  // (20-11: escalas_adaptables..., src\Escalas.cs)
            if (CeParseLatencia(key, val)) return true; // (21-11, 1.2.1: mpv_cola_ms, audio_bloque_ms, audio_cola_*_ms, src\Latencia.cs)
            if (CeParseRitmo(key, val)) return true;    // (22-11, 1.2.2: magpie_vrr_30*, magpie_fijo_30_dlss, magpie_ritmo_margen_fg..., src\Ritmo.cs)
            if (CeParseMult(key, val)) return true;     // (1.2.2: generacion_multiplicador, xess_fg_1080, src\Lanzador\Multiplicador.cs)
            if (key == "motor_fg_multi") { MotorFgMulti = val.Trim() != "0"; return true; }   // (1.2.4 MOTORFG: src\Lanzador\MotorFg.cs)
            if (CeParseReescalado(key, val)) return true; // (22-11, 1.2.2: magpie_nitidez_transformer..., src\Lanzador\Reescalado.cs)
            switch (key)
            {
                case "device_path": DevicePath = val.Trim(); return true;
                case "audio_dispositivo": AudioDevice = val.Trim(); return true;
                case "audio_pines": AudioPins = val.Trim() == "1"; return true;
                case "gpu_fabricante": GpuVendor = val.Trim().ToLowerInvariant(); Ce.NoNvidia = GpuVendor.Length > 0 && GpuVendor != "nvidia"; return true;
                case "motor_streamline": EngineStreamline = val.Trim().ToLowerInvariant(); return true;
                case "motor_adaptador": EngineAdapter = val.Trim(); return true;
                case "generacion_disponible": FgAvailable = val.Trim() != "0"; if (!FgAvailable) FgOff = true; return true;
                case "dlssg_solo_magpie": Ce.DlssgSoloMagpie = val.Trim() == "1"; return true;   // (1.0.10: RTX 30 + dlssg_sm86)
                case "idioma": Language = val.Trim().ToLowerInvariant(); Idioma.Poner(Language); return true;
                // 21-11 (1.2.1): la escribe la app (src\Lanzador\DefenderAviso.cs: el ultimo aviso de Defender ya enseñado) y la lee ella misma
                // con Ce.IniLeer; aqui solo para que el log no diga "Aviso: clave desconocida en ini: defender_aviso" en cada arranque
                case "defender_aviso": return true;
            }
            return false;
        }
        // (FgMagpieOnly: desde el 19-11 lo trae Prisma, Switch2Magpie.cs, GENERACION)
    }

    static partial class Ce
    {
        // Sin NVIDIA: nada de NVAPI (perfiles, Smooth Motion); lo pone gpu_fabricante del ini
        public static volatile bool NoNvidia;
        // (1.0.10) RTX 30 con dlssg_sm86 (ini dlssg_solo_magpie=1): DLSS FG solo por Magpie (el DLSS-G del motor, Streamline, pide RTX 40)
        public static volatile bool DlssgSoloMagpie;

        public static string BaseIni { get { return Path.Combine(Paths.AppDir, @"resources\base.ini"); } }
        public static string HardwareFile { get { return Path.Combine(Paths.Datos, "hardware.txt"); } }
        public static string EngineLogDir { get { return Path.Combine(Paths.Datos, "logs"); } }

        // ------------------------------------------------------------------ el motor
        // El escalador del motor: DLSS si lo hay; si no (o el alternativo de R3), FSR 1 (EASU + RCAS, shader propio del motor)
        public static string EngineUpscaler(Config cfg)
        {
            string s = cfg.ScalerNow;
            if (s == "dlss") return NoNvidia ? "fsr1" : "dlss";
            // 21-11 (1.0.9, src\Motor\Engine\EngineSr.cs): XeSS SR y FSR 3.1 / 4 ya los hace el motor propio (con las DLL de magpie\)
            if (s == "xess") return "xess";
            if (s == "fsr4") return "fsr4";
            if (s == "fsr2" || s == "fsr3") return "fsr3";
            if (s == "fsr") return "fsr1";
            if (s == "ninguno") return "catrom";   // (1.2.01: sin reescalado = estirado simple, sin IA)
            return "fsr1";   // (R3 / sin DLSS: el FSR 1 propio del motor; catrom queda en el motor por si se pide a mano)
        }

        public static string[] EngineArgsExtra(Config cfg)
        {
            List<string> a = new List<string>();
            if (cfg.DevicePath.Length > 0 && cfg.TestSource.Length == 0) a.Add("device_path=" + cfg.DevicePath);
            a.Add("streamline=" + (NoNvidia ? "0" : cfg.EngineStreamline));
            string luid = AdaptadorMotor.Luid(cfg.EngineAdapter);   // (22-11, 1.2.2: el LUID de AHORA de esa grafica; src\Lanzador\AdaptadorMotor.cs)
            if (luid.Length > 0) a.Add("adapter_luid=" + luid);
            a.Add("datos=" + EngineLogDir);
            a.Add("dlls_sr=" + Paths.MagpieDir);   // 21-11 (1.0.9): libxess.dll / amd_fidelityfx_*.dll para XeSS y FSR 3/4 en el motor
            Escalas.ArgsMotor(cfg, a);   // (20-11: adapta=1 adapta_clave=...: el motor ajusta las escalas solo, src\Escalas.cs)
            CeImagen.ArgsMotor(cfg, a);  // (20-11: vrr=1, src\ImagenCe.cs)
            return a.ToArray();
        }

        // ------------------------------------------------------------------ arranque: primer arranque, asistente, cambios de hardware
        // -1 = seguir (leer el ini y arrancar); otro = salir con ese codigo
        public static int Arranque(string[] args, bool dryRun)
        {
            // (20-11) --salir: la app abierta se cierra como con Salir de la bandeja (pruebas sin tocar la bandeja; ImagenCe.cs CeSalidaRemota)
            foreach (string a in args)
                if (a == "--salir")
                {
                    System.Threading.EventWaitHandle ev;
                    if (!System.Threading.EventWaitHandle.TryOpenExisting("Local\\ConsoleEnhancer_Salir", out ev)) return 1;
                    using (ev) ev.Set();
                    return 0;
                }
            // (20-11) el exe elevado de RTX HDR: Allow y Driver Flags en los perfiles de NVIDIA (piden administrador) y salir (ImagenCe.cs)
            foreach (string a in args)
                if (a.StartsWith("--ce-rtx-hdr-admin="))
                {
                    uint fl;
                    if (!uint.TryParse(a.Substring(19), NumberStyles.Integer, CultureInfo.InvariantCulture, out fl)) fl = 2;
                    int r = NvDrsCe.PonerAdmin(CeImagen.Exes, fl);
                    Log.Write("RTX HDR (administrador): Allow y Driver Flags 0x" + fl.ToString("X2") + " -> NVAPI " + r);
                    return r == 0 ? 0 : 1;
                }
            try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); } catch (InvalidOperationException) { }
            ExtrasGuardados.AlArrancar();   // (22-11, 1.2.3 MOTOR5: lo que puso el usuario y no va en el zip, de %LOCALAPPDATA% si falta; src\Lanzador\ExtrasGuardados.cs)
            PrepararExtras();
            bool forzar = false, auto = false;
            string simula = null;   // pruebas: --simula=<caso de Deteccion.Simulador> (con --asistente-auto): el plan de otro PC
            foreach (string a in args)
            {
                if (a == "--asistente") forzar = true;
                else if (a == "--asistente-auto") auto = true;
                else if (a.StartsWith("--simula=")) simula = a.Substring(9);
            }
            bool nuevo = !File.Exists(Paths.IniFile);
            if (!nuevo && !forzar && !auto && !dryRun && simula == null) ActualizarIni();   // 21-11 (1.0.7)
            if (!nuevo && !forzar && !auto)
            {
                // cambio de hardware: la huella de la ultima deteccion
                string vieja = LeerHuella();
                if (vieja == null) return -1;
                Hardware rapido = Detectar(false);
                string huella = Huella(rapido);
                if (huella == vieja) return -1;
                if (PantallaSinIdentificar(vieja, huella))
                {
                    Log.Write("Hardware: la pantalla principal no se identifica ahora (" + huella + "; apagada o en otra entrada, sin EDID): no cuenta como un cambio de hardware");
                    return -1;
                }
                Log.Write("Hardware distinto del de la ultima vez (" + vieja + " -> " + huella + ")");
                if (dryRun) return -1;
                DialogResult r = MessageBox.Show(Tx.T("Ha cambiado el hardware (gráfica, pantalla o capturadora).\n¿Abrir el asistente para adaptar ConsoleEnhancer?",
                                                      "Your hardware changed (GPU, display or capture device).\nOpen the setup wizard to adapt ConsoleEnhancer?"),
                                                 "ConsoleEnhancer", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { GuardarHuella(rapido, null); return -1; }
                return Asistente.Ejecutar(rapido, false) ? -1 : 0;
            }
            Hardware hw;
            if (simula != null)
            {
                OpcionesDeteccion so = Simulador.Opciones(simula, Opciones(false));
                hw = Hardware.Detectar(so);
                Simulador.Aplicar(hw, simula, so);
                Log.Write("Deteccion SIMULADA: " + simula);
            }
            else hw = Detectar(false);
            if (auto || dryRun)
            {
                PreferenciasPlan pref = new PreferenciasPlan();
                pref.Consola = "";
                if (simula == null) CeImagen.Preferencias(pref, hw.Pantallas.Find(delegate(PantallaInfo x) { return x.Principal; }));   // (20-11: VRR auto)
                PlanImagen plan = Planificador.Elegir(hw, pref);
                EscribirIni(plan, hw, "auto", Tx.Idioma, null);
                Log.Write("Asistente automatico: " + plan.Informe().Replace("\r\n", " / "));
                return -1;
            }
            return Asistente.Ejecutar(hw, nuevo) ? -1 : 0;
        }

        // Extras no oficiales que pone el usuario en <app>\extras (DLSS 5): Magpie solo carga la DLL junto a su exe -> se copia a magpie\
        // (y se dice que no es oficial). Si el usuario la quita de extras\, se quita tambien la copia que puso la app (marca .de_extras)
        public static void PrepararExtras()
        {
            try
            {
                string ex = Path.Combine(Paths.AppDir, @"extras\" + Integrado.Dlss5Dll), dst = Path.Combine(Paths.MagpieDir, Integrado.Dlss5Dll), marca = dst + ".de_extras";
                if (File.Exists(ex) && Directory.Exists(Paths.MagpieDir) && (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(ex).Length))
                {
                    File.Copy(ex, dst, true); File.WriteAllText(marca, ex);
                    Log.Write("DLSS 5: " + Integrado.Dlss5Dll + " de extras\\ copiada a magpie\\ (DLL de NVIDIA modificada por la comunidad, SIN FIRMA, no oficial)");
                }
                else if (!File.Exists(ex) && File.Exists(marca) && File.Exists(dst)) { File.Delete(dst); File.Delete(marca); Log.Write("DLSS 5: ya no esta en extras\\: quitada de magpie\\"); }
            }
            catch (Exception e) { Log.Error("Extras (DLSS 5)", e); }
            // (19-11) DLSS-G en RTX 30: runtimes\dlssg_sm86 de la comunidad (DLL autofirmadas). Igual: extras\runtimes\dlssg_sm86 (o
            // extras\dlssg_sm86) -> magpie\runtimes\dlssg_sm86, con su marca; si se quita de extras\, se quita la copia
            try
            {
                string ex = Path.Combine(Paths.AppDir, @"extras\runtimes\dlssg_sm86");
                if (!Directory.Exists(ex)) ex = Path.Combine(Paths.AppDir, @"extras\dlssg_sm86");
                string dst = Path.Combine(Paths.MagpieDir, @"runtimes\dlssg_sm86"), marca = dst + ".de_extras";
                // (1.2.02: tambien si una copia anterior quedo a medias, sin version.dll). (1.2.08) Solo si es la version que valida Magpie
                // (si no, DLSS FG no arranca: Componentes.AsegurarSm86 la quita y pone la oficial)
                if (Directory.Exists(ex) && Directory.Exists(Paths.MagpieDir) && !File.Exists(Path.Combine(dst, "version.dll")) && Componentes.Sm86Distinto(ex) == null)
                {
                    int n = CopiarCarpeta(ex, dst);
                    File.WriteAllText(marca, ex);
                    Log.Write("DLSS-G en RTX 30: runtimes\\dlssg_sm86 de extras\\ copiado a magpie\\ (" + n + " ficheros; de la comunidad, DLL autofirmadas, no oficial)");
                }
                else if (!Directory.Exists(ex) && File.Exists(marca))
                {
                    if (Directory.Exists(dst)) Directory.Delete(dst, true);
                    File.Delete(marca);
                    Log.Write("DLSS-G en RTX 30: ya no esta en extras\\: quitado runtimes\\dlssg_sm86 de magpie\\");
                }
            }
            catch (Exception e) { Log.Error("Extras (dlssg_sm86)", e); }
            // (1.2.07, el usuario: "no tiene que hacer falta poner ningun archivo para activarlo ... que lo incluya la app"): con DLSS 5 en
            // una NVIDIA y sin dlssg_sm86, la app lo busca en el PC o lo baja sola de su release (Componentes.AsegurarSm86, en segundo plano)
            Componentes.AsegurarSm86();
        }

        static int CopiarCarpeta(string de, string a)
        {
            int n = 0;
            Directory.CreateDirectory(a);
            foreach (string f in Directory.GetFiles(de)) { File.Copy(f, Path.Combine(a, Path.GetFileName(f)), true); n++; }
            foreach (string d in Directory.GetDirectories(de)) n += CopiarCarpeta(d, Path.Combine(a, Path.GetFileName(d)));
            return n;
        }

        // --esperar-salida (el asistente pedido desde la bandeja): hasta 15 s a que se cierre la app de antes
        public static void EsperarSalida()
        {
            for (int i = 0; i < 75; i++)
            {
                System.Threading.Mutex m;
                if (!System.Threading.Mutex.TryOpenExisting("Local\\ConsoleEnhancer_SingleInstance", out m)) return;
                m.Dispose();
                System.Threading.Thread.Sleep(200);
            }
        }

        public static OpcionesDeteccion Opciones(bool abrir)
        {
            OpcionesDeteccion op = new OpcionesDeteccion();
            op.AbrirCapturadoras = abrir;
            op.RutasDlssNr = OpcionesDeteccion.RutasExtra(Paths.AppDir, Integrado.Dlss5Dll);
            op.RutasDlssgSm86 = OpcionesDeteccion.RutasExtra(Paths.AppDir, @"runtimes\dlssg_sm86");
            return op;
        }

        public static Hardware Detectar(bool abrir)
        {
            Hardware hw = Hardware.Detectar(Opciones(abrir));
            foreach (string e in hw.Errores) Log.Write("Deteccion: " + e);
            Log.Write("Deteccion (" + string.Join(", ", hw.Tiempos.ToArray()) + ")");
            return hw;
        }

        // ------------------------------------------------------------------ huella del hardware
        public static string Huella(Hardware hw)
        {
            PantallaInfo p = hw.Pantallas.Find(delegate(PantallaInfo x) { return x.Principal; });
            GpuInfo g = hw.GpuDe(p);
            CapturadoraInfo c = hw.Capturadoras.Find(delegate(CapturadoraInfo x) { return x.Tipo == TipoCaptura.Capturadora; });
            return (g != null ? g.Clave : "-") + "|" + (p != null ? p.Pnp + " " + p.Ancho + "x" + p.Alto : "-") + "|" + (c != null ? c.Instancia : "-");
        }

        // 22-11 (1.2.2, ERRORES): con la tele apagada (o en otra entrada) Windows no da su identificador (EDID): la huella salia
        // "10DE:2803:...| 3840x2160|..." en vez de "...|MEIA699 3840x2160|..." y la app preguntaba "Ha cambiado el hardware... ¿Abrir el
        // asistente?" al abrirse (con Windows, al despertar), aunque fuera la misma tele (visto en este PC el 22-11 con la tele apagada).
        // Misma grafica y misma capturadora y la pantalla de ahora sin identificar: no es otro hardware
        static bool PantallaSinIdentificar(string vieja, string nueva)
        {
            string[] a = (vieja ?? "").Split('|'), b = (nueva ?? "").Split('|');
            if (a.Length != 3 || b.Length != 3 || a[0] != b[0] || a[2] != b[2]) return false;
            return b[1] == "-" || b[1].StartsWith(" ", StringComparison.Ordinal);
        }

        static string LeerHuella()
        {
            try
            {
                if (!File.Exists(HardwareFile)) return null;
                foreach (string l in File.ReadAllLines(HardwareFile)) if (l.StartsWith("huella=")) return l.Substring(7);
            }
            catch { }
            return null;
        }

        public static void GuardarHuella(Hardware hw, PlanImagen plan)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("huella=" + Huella(hw));
                sb.AppendLine("fecha=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine(hw.Informe());
                if (plan != null) sb.AppendLine(plan.Informe());
                File.WriteAllText(HardwareFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Error("Guardando " + HardwareFile, ex); }
        }

        // ------------------------------------------------------------------ al actualizar: el ini al dia
        // 21-11 (1.0.7): base.ini solo se escribia en el ini con el asistente (o al cambiar el hardware): quien actualizaba (o heredaba
        // el ini de la version anterior, Integrado.HeredarDatos) seguia con los ajustes generales viejos (en la 1.0.6: los vectores
        // completos y el giro de la 1.0.5 encendidos -> halos; Smooth Motion de serie en RTX 40). Una vez por version (la marca
        // "# ce_ini_version=" al final): lo de arriba del plan pasa a ser el base.ini de ahora, el plan y lo de debajo se quedan, y se
        // añade la generacion de un plan nuevo de este PC (lo demas del plan, lo elegido en el asistente, no se toca). Copia: .antes-<version>
        // 22-11 (1.2.2): base.ini con valores de serie nuevos (dlss5_antiparpadeo_30_1pasada=3, magpie_nitidez_transformer, dlss_flujo_transformer,
        // color / detalle / contener de 2-3 pasadas a 4K, enfoque_sin_reescalado, motor_uso_grafica, espera_sonda_cada_ms) y, en graficas lentas,
        // el plan pone tabla_ligero_30* (Escalas.cs). (1.0.10): DLSS FG en RTX 30 con dlssg_sm86 (dlssg_solo_magpie); (1.0.9) capturas_120 y XeSS FG
        // 22-11 (1.2.3): motor_dlss5=auto y motor_dlss5_pasadas (DLSS 5 en el motor con engine\PrismaNr.dll), la escala de DLSS 5 por el hilo de
        // Magpie (dlss5_escala_hilo / dlss5_escala_grafica / generacion_dlss5_minimo) y comentarios del ritmo (magpie_fijo_30*)
        public const string IniVersion = "1.2.3";
        static readonly string[] ClavesGenPlan = { "generacion_tipo", "generacion_tipo_60", "generacion_tipo_30", "motor_generacion_60", "motor_generacion_30",
                                                   // (1.2.2, UNIVERSAL: el modo ligero de Magpie en el 30 con una grafica bastante mas lenta que la de referencia)
                                                   "tabla_ligero_30_fg", "tabla_ligero_30", "tabla_ligero_30_fg_4k", "tabla_ligero_30_4k",
                                                   "dlssg_solo_magpie",
                                                   "generacion_lista_60", "generacion_lista_30", "generacion_60", "generacion_disponible",
                                                   // 21-11 (1.0.9; el usuario: "no hay opcion de 120fps"): los ini con el plan de antes del 20-11 no tenian
                                                   // capturas_120 (sin ella no hay modo 120): se ponen tambien al dia (con la capturadora abierta para leer sus formatos)
                                                   "capturas_120", "framerate_120",
                                                   // (1.0.9: XeSS / FSR 4 de serie sin DLSS; con RTX sale igual que antes, dlss)
                                                   "escalador", "escalador_alternativo" };
        static void ActualizarIni()
        {
            try
            {
                string ini = Paths.IniFile;
                if (!File.Exists(ini) || !File.Exists(BaseIni)) return;
                string t = File.ReadAllText(ini);
                string marca = "# ce_ini_version=" + IniVersion;
                if (t.Contains(marca)) return;
                int ip = t.IndexOf("# ===== Plan de ConsoleEnhancer", StringComparison.Ordinal);
                if (ip < 0) { Log.Write("Ini: sin el bloque del plan, no se pone al dia (" + ini + ")"); return; }
                string resto = t.Substring(ip);
                // 22-11 (1.2.2, REVISION): lo que va despues de la marca de la actualizacion anterior se conserva (Ce.IniPoner pone al final las
                // claves que no estaban: defender_aviso...; antes se perdia y el aviso de Defender volvia a salir). Y lo elegido que vive arriba,
                // en las lineas del base.ini viejo (IniPoner cambia esa linea: apagar_pc_modo desde la bandeja, motor_dlss5...), tambien
                string cola = "";
                int ia = resto.IndexOf("# ===== Actualizacion a la ", StringComparison.Ordinal);
                if (ia >= 0)
                {
                    int im = resto.IndexOf("# ce_ini_version=", ia, StringComparison.Ordinal);
                    int eol = im >= 0 ? resto.IndexOf('\n', im) : -1;
                    if (eol >= 0) cola = resto.Substring(eol + 1);
                    resto = resto.Substring(0, ia);
                }
                Dictionary<string, string> elegidas = new Dictionary<string, string>();
                foreach (string k in ClavesElegidas) { string v = IniLeer(k); if (v != null) elegidas[k] = v; }
                // (22-11, 1.2.3 REVISION3) motor_dlss5=0 era el valor de serie del base.ini hasta la 1.2.2 y nada lo elegia ("Añadir DLSS 5"
                // solo escribia el 1; "Quitar DLSS 5", el 0 al quitarla): en un ini de antes de la 1.2.3 ese 0 no es una eleccion, y se quedaba
                // encima del auto nuevo (con engine\PrismaNr.dll DLSS 5 seguia por Magpie tras actualizar; simulado con el ini del usuario: la
                // ultima linea salia motor_dlss5=0). Desde la 1.2.3 el 0 si se elige (menu de ajustes, bandeja, "Añadir DLSS 5" -> No): se conserva
                string md5;
                if (elegidas.TryGetValue("motor_dlss5", out md5) && md5.Trim() == "0" && IniAnteriorA(t, 1, 2, 3)) elegidas.Remove("motor_dlss5");
                StringBuilder gen = new StringBuilder();
                try
                {
                    Hardware hw = Detectar(true);   // (1.0.9: abriendo la capturadora: sus formatos a 120 para capturas_120)
                    PreferenciasPlan pref = new PreferenciasPlan();
                    pref.Consola = "";
                    CeImagen.Preferencias(pref, hw.Pantallas.Find(delegate(PantallaInfo x) { return x.Principal; }));
                    PlanImagen plan = Planificador.Elegir(hw, pref);
                    string pi = PlanComoIni(plan, hw, IniLeer("consola"), IniLeer("idioma"), null);
                    // (si no se pudieron leer los formatos: capturas_120 vacia no se pone, se queda la que hubiera)
                    if (System.Text.RegularExpressions.Regex.IsMatch(pi, @"(?m)^capturas_120=\s*$")) pi = pi.Replace("capturas_120=", "#capturas_120=");
                    foreach (string raw in pi.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    {
                        int i = raw.IndexOf('=');
                        if (i > 0 && !raw.StartsWith("#") && Array.IndexOf(ClavesGenPlan, raw.Substring(0, i).Trim()) >= 0) gen.AppendLine(raw.Trim());
                    }
                }
                catch (Exception ex) { Log.Error("Ini al dia: plan nuevo para la generacion", ex); }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ConsoleEnhancer - ajustes (clave=valor; '#' = comentario). Lo de abajo del todo lo escribe el asistente segun tu hardware;");
                sb.AppendLine("# puedes cambiar cualquier clave (la ultima linea de cada clave es la que vale). Asistente otra vez: menu de la bandeja.");
                sb.AppendLine();
                sb.Append(File.ReadAllText(BaseIni)).AppendLine();
                sb.AppendLine();
                sb.Append(resto.TrimEnd()).AppendLine();
                sb.AppendLine();
                sb.AppendLine("# ===== Actualizacion a la " + IniVersion + " (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "): arriba el base.ini nuevo; aqui la generacion de un plan nuevo =====");
                sb.Append(gen.ToString());
                sb.AppendLine(marca);
                // (lo conservado, detras de la marca: en la siguiente actualizacion se conserva igual; sin las claves de la generacion del plan)
                StringBuilder detras = new StringBuilder();
                foreach (string raw in cola.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    string l = raw.Trim();
                    int i = l.IndexOf('=');
                    if (l.Length == 0 || l.StartsWith("# ce_ini_version=") || (i > 0 && !l.StartsWith("#") && Array.IndexOf(ClavesGenPlan, l.Substring(0, i).Trim()) >= 0)) continue;
                    detras.AppendLine(l);
                }
                Dictionary<string, string> ahora = UltimosValores(sb.ToString() + detras.ToString());
                foreach (KeyValuePair<string, string> kv in elegidas)
                {
                    string v;
                    if (!ahora.TryGetValue(kv.Key, out v) || v != kv.Value) detras.AppendLine(kv.Key + "=" + kv.Value);
                }
                sb.Append(detras.ToString());
                try { File.Copy(ini, ini + ".antes-" + IniVersion, true); } catch { }
                File.WriteAllText(ini, sb.ToString(), new UTF8Encoding(false));
                Log.Write("Ini al dia para la " + IniVersion + ": base.ini nuevo y la generacion del plan (" + gen.ToString().Replace("\r\n", " ").Trim() + ")" +
                          (detras.Length > 0 ? "; se conserva " + detras.ToString().Replace("\r\n", " ").Trim() : "") + "; copia en " + ini + ".antes-" + IniVersion);
            }
            catch (Exception ex) { Log.Error("Poniendo el ini al dia", ex); }
        }

        // (22-11, 1.2.2) lo elegido en la bandeja / el panel / el asistente que puede estar en una linea del base.ini viejo (ActualizarIni)
        static readonly string[] ClavesElegidas = { "apagar_pc_modo", "apagar_pc_aviso_s", "motor_dlss5", "escritorio_negro" };

        // (1.2.3 REVISION3) ¿La marca "# ce_ini_version=" del ini (su ultima puesta al dia) es de antes de la a.b.c? Sin marca: si (de antes
        // de la 1.0.7). Las 1.2.01-1.2.09 van entre la 1.2.0 y la 1.2.1 (el "0x" cuenta como 0)
        static bool IniAnteriorA(string texto, int a, int b, int c)
        {
            System.Text.RegularExpressions.MatchCollection ms = System.Text.RegularExpressions.Regex.Matches(texto ?? "", @"(?m)^# ce_ini_version=(\d+)\.(\d+)(?:\.(\d+))?");
            if (ms.Count == 0) return true;
            System.Text.RegularExpressions.Match m = ms[ms.Count - 1];
            int x = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), y = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            string zs = m.Groups[3].Success ? m.Groups[3].Value : "0";
            int z = zs.Length > 1 && zs[0] == '0' ? 0 : int.Parse(zs, CultureInfo.InvariantCulture);
            if (x != a) return x < a;
            if (y != b) return y < b;
            return z < c;
        }

        // El valor que vale de cada clave en un texto de ini (la ultima linea)
        static Dictionary<string, string> UltimosValores(string texto)
        {
            Dictionary<string, string> r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in texto.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l[0] == '#' || l[0] == ';') continue;
                int i = l.IndexOf('=');
                if (i > 0) r[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
            }
            return r;
        }

        // ------------------------------------------------------------------ el plan -> el ini
        // base.ini (lo general) + el bloque del plan (lo de este hardware; va al final: en el ini manda la ultima linea de cada clave).
        // audioElegido: null = el del plan
        // inicio / apagar: lo elegido en el ultimo paso del asistente (iniciar_con_windows, apagar_pc); null = lo que hubiera en el ini
        public static void EscribirIni(PlanImagen plan, Hardware hw, string consola, string idioma, AudioEntrada audioElegido, bool? inicio = null, bool? apagar = null, bool? escritorio = null, OpcionesImagen imagen = null)
        {
            string iniAntes = IniLeer("iniciar_con_windows"), apagarAntes = IniLeer("apagar_pc"), avisoAntes = IniLeer("apagar_pc_aviso_s"), modoAntes = IniLeer("apagar_pc_modo"), escAntes = IniLeer("escritorio_negro");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# ConsoleEnhancer - ajustes (clave=valor; '#' = comentario). Lo de abajo del todo lo escribe el asistente segun tu hardware;");
            sb.AppendLine("# puedes cambiar cualquier clave (la ultima linea de cada clave es la que vale). Asistente otra vez: menu de la bandeja.");
            sb.AppendLine();
            try
            {
                if (File.Exists(BaseIni)) sb.Append(File.ReadAllText(BaseIni)).AppendLine();
                else Log.Write("Sin " + BaseIni + ": solo el plan");
            }
            catch (Exception ex) { Log.Error("Leyendo " + BaseIni, ex); }
            sb.AppendLine();
            sb.Append(PlanComoIni(plan, hw, consola, idioma, audioElegido, imagen));
            // (19-11) lo de arrancar y apagar con la consola: lo elegido en el asistente o lo de antes (menu de la bandeja)
            sb.AppendLine();
            sb.AppendLine("# ===== Inicio con Windows y apagado (asistente o menu de la bandeja) =====");
            sb.AppendLine("iniciar_con_windows=" + (inicio.HasValue ? (inicio.Value ? "1" : "0") : iniAntes == "1" ? "1" : "0"));
            sb.AppendLine("apagar_pc=" + (apagar.HasValue ? (apagar.Value ? "1" : "0") : apagarAntes == "1" ? "1" : "0"));
            if (!string.IsNullOrEmpty(avisoAntes)) sb.AppendLine("apagar_pc_aviso_s=" + avisoAntes);
            if (!string.IsNullOrEmpty(modoAntes)) sb.AppendLine("apagar_pc_modo=" + modoAntes);
            sb.AppendLine("escritorio_negro=" + (escritorio.HasValue ? (escritorio.Value ? "1" : "0") : escAntes == "1" ? "1" : "0"));
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.IniFile));
            if (File.Exists(Paths.IniFile))
            {
                try { File.Copy(Paths.IniFile, Paths.IniFile + ".anterior", true); } catch { }
            }
            File.WriteAllText(Paths.IniFile, sb.ToString(), new UTF8Encoding(false));
            // generacion de serie (el boton de captura la cambia luego: games\framegen.txt)
            try { File.WriteAllText(Paths.FgFile, plan.GenDeSerie ? "1" : "0"); } catch { }
            GuardarHuella(hw, plan);
            Log.Write("Ini escrito con el plan: " + Paths.IniFile);
        }

        public static string PlanComoIni(PlanImagen p, Hardware hw, string consola, string idioma, AudioEntrada audioElegido, OpcionesImagen opciones = null)
        {
            StringBuilder sb = new StringBuilder();
            GpuInfo g = p.Gpu;
            sb.AppendLine("# ===== Plan de ConsoleEnhancer para este PC (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ") =====");
            foreach (string m in p.Motivos) sb.AppendLine("#  - " + m);
            foreach (string m in p.Avisos) sb.AppendLine("#  ! " + m);
            string fab = g == null ? "otro" : g.Fab == Fabricante.Nvidia ? "nvidia" : g.Fab == Fabricante.Amd ? "amd" : g.Fab == Fabricante.Intel ? "intel" : "otro";
            sb.AppendLine("idioma=" + (string.IsNullOrEmpty(idioma) ? "auto" : idioma));
            sb.AppendLine("gpu_fabricante=" + fab);
            if (g != null && !g.Software) sb.AppendLine("motor_adaptador=" + g.Luid.ToString("X"));
            sb.AppendLine("consola=" + (string.IsNullOrEmpty(consola) ? "auto" : consola));
            if (p.Captura != null)
            {
                sb.AppendLine("device=" + p.Captura.Nombre);
                sb.AppendLine("device_path=" + p.Captura.DevicePath);
                sb.AppendLine("video_size=" + Tamano(p.CapW, p.CapH));
                sb.AppendLine("framerate=" + p.CapFps);
                string pf = p.CapFormato == "NV12" ? "nv12" : p.CapFormato == "YUY2" ? "yuyv422" : p.CapFormato == "RGB24" ? "bgr24" : p.CapFormato == "RGB32" ? "bgr0" : "";
                sb.AppendLine("pixel_format=" + pf);
                // (fusion I, modo 120 de Prisma) las capturas que ESTA capturadora lista a 120 (Plan.Capturas120); vacio = sin modo 120
                string fr120;
                sb.AppendLine("capturas_120=" + p.Capturas120(out fr120));
                if (fr120.Length > 0) sb.AppendLine("framerate_120=" + fr120);
            }
            AudioEntrada au = audioElegido ?? p.Audio;
            sb.AppendLine("console_audio=" + (au != null ? "1" : "0"));
            if (au != null)
            {
                sb.AppendLine("audio_dispositivo=" + au.Nombre);
                sb.AppendLine("audio_pines=" + (au.Origen == "dshow-pin" ? "1" : "0"));
            }
            sb.AppendLine("home_4k_nativo=" + (p.HomeNativo ? "1" : "0"));
            // motor y escalado
            sb.AppendLine("motor=" + (p.Motor == "propio" ? "propio" : "magpie"));
            sb.AppendLine("motor_streamline=" + (p.Streamline ? "auto" : "0"));
            bool nv = fab == "nvidia";
            sb.AppendLine("escalador=" + (p.Escalador == "dlss" || p.Escalador == "xess" || p.Escalador == "fsr4" ? p.Escalador : p.Escalador == "ninguno" ? (nv && g != null && g.F.DlssSr.Si ? "dlss" : "fsr") : "fsr"));
            // (1.0.9) con XeSS / FSR 4 de serie, R3 = el otro (FSR 4 -> XeSS) o FSR 1
            string alt = p.Escalador == "fsr4" ? (p.EscaladorAlternativo == "xess" ? "xess" : "fsr") : p.Escalador == "xess" ? "fsr" :
                         nv && g != null && g.F.RtxVideo.Si ? "vsr" : g != null && g.F.XessSr.Si && g.Fab == Fabricante.Intel ? "xess" : "lanczos";
            sb.AppendLine("escalador_alternativo=" + alt);
            string flujo = nv && g != null && g.F.Nvof.Si ? "nvidia" : "amd";
            sb.AppendLine("dlss_flujo=" + flujo);
            sb.AppendLine("dlss5_flujo=" + flujo);
            sb.AppendLine("fg_flujo_30=" + flujo);
            if (!nv) { sb.AppendLine("flujo_calidad_amd=1"); sb.AppendLine("rtx_hdr=0"); sb.AppendLine("reflex=0"); }
            foreach (KeyValuePair<string, ModoPlan> kv in p.Modos) if (kv.Value.Modelo.Length > 0) sb.AppendLine("motor_modelo_" + kv.Key + "=" + kv.Value.Modelo);
            // generacion
            string g60 = p.Gen60, g30 = p.Gen30;
            bool magpieFg = g60 == "fsr3fg" || g60 == "xessfg" || g30 == "fsr3fg" || g30 == "xessfg";
            bool altFsr3 = p.GenAlternativa.IndexOf("FSR 3", StringComparison.Ordinal) >= 0;
            bool engineFg = g60 == "dlssg" || g60 == "smooth" || g30 == "dlssg" || g30 == "smooth";
            sb.AppendLine("motor_generacion_60=" + (g60 == "smooth" ? "smooth" : "dlss"));
            sb.AppendLine("motor_generacion_30=" + (g30 == "smooth" ? "smooth" : "dlss"));
            if (engineFg)
            {
                sb.AppendLine("generacion_tipo=dlss");
                sb.AppendLine("generacion_tipo_60=" + (g60 == "smooth" ? "smooth" : "dlss"));
                sb.AppendLine("generacion_tipo_30=" + (g30 == "smooth" ? "smooth" : "dlss"));
                sb.AppendLine("generacion_60=" + Math.Max(2, Math.Min(4, p.Mult60)));
                sb.AppendLine("generacion_disponible=1");
            }
            else if (p.DlssgSoloMagpie)
            {
                // (1.0.10) RTX 30 con dlssg_sm86: DLSS FG por Magpie, apagada de serie (framegen.txt = 0: la enciende el boton, Ctrl+G o la bandeja)
                sb.AppendLine("generacion_tipo=dlss");
                sb.AppendLine("generacion_tipo_60=dlss");
                sb.AppendLine("generacion_tipo_30=dlss");
                sb.AppendLine("generacion_60=2");
                sb.AppendLine("generacion_disponible=1");
            }
            else if (magpieFg || altFsr3)
            {
                string t = g60 == "xessfg" || g30 == "xessfg" ? "xess" : "fsr3";
                sb.AppendLine("generacion_tipo=" + t);
                sb.AppendLine("generacion_tipo_60=" + t);
                sb.AppendLine("generacion_tipo_30=" + t);
                sb.AppendLine("generacion_disponible=1");
            }
            else sb.AppendLine("generacion_disponible=0");
            sb.AppendLine("dlssg_solo_magpie=" + (p.DlssgSoloMagpie ? "1" : "0"));
            sb.AppendLine("modo30_generacion=2");
            // (19-11) el selector de L3 / Ctrl+G: los tipos de generacion que tiene esta grafica (Plan.ListasGeneracion)
            sb.AppendLine("generacion_lista_60=" + (p.Lista60.Count > 0 ? string.Join(",", p.Lista60.ToArray()) : "no"));
            sb.AppendLine("generacion_lista_30=" + (p.Lista30.Count > 0 ? string.Join(",", p.Lista30.ToArray()) : "no"));
            sb.AppendLine("motor_en_espera=" + (p.MotorEnEspera ? "1" : "0"));
            sb.AppendLine("# DLSS 5: " + (p.Dlss5Disponible ? "disponible (DLL del usuario, no oficial)" : "no disponible") + (p.Dlss5Nota.Length > 0 ? " - " + p.Dlss5Nota : ""));
            // (20-11) las escalas de esta grafica (no las medidas en el PC de Prisma) y la imagen: HDR, RTX HDR, VRR, latencia
            sb.Append(Escalas.BloqueIni(p));
            sb.Append(CeImagen.BloqueIni(p, opciones));
            return sb.ToString();
        }

        // Las capturas que maneja la app (cruceta del mando): la mas cercana por alto
        static string Tamano(int w, int h)
        {
            if (h >= 2160) return "3840x2160";
            if (h >= 1440) return "2560x1440";
            if (h >= 1080 || h == 0) return "1920x1080";
            return "1280x720";
        }
    }

    // ------------------------------------------------------------------ idioma de los textos de pantalla
    // Los textos de pantalla del lanzador estan escritos en espanol; en ingles se traducen al salir (avisos, telon, bandeja) con
    // languages\en.txt: "texto en espanol<TAB>english text" por linea. Primero la frase entera; si no esta, trozos conocidos (los mas
    // largos antes). Lo que no este en la tabla sale en espanol
    static class Idioma
    {
        static volatile bool ingles;
        static List<string[]> tabla;
        static Dictionary<string, string> exactas;
        static readonly object sync = new object();

        public static bool Ingles { get { return ingles; } }

        // 21-11 (1.2.06, el usuario: "al abrir la app [que este en ingles] y ahi pueda elegir el idioma"): de serie en ingles (antes la
        // lengua de Windows). auto = lo ultimo elegido (data\language.txt: en la primera ventana, el asistente o la bandeja); si nunca se
        // ha elegido, ingles
        public static void Poner(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            bool en = v == "en" || (v != "es" && Guardado() != "es");
            ingles = en;
            Tx.Ingles = en;
        }

        static string Fichero { get { return Path.Combine(Paths.Datos, "language.txt"); } }

        static string Guardado()
        {
            try { return File.Exists(Fichero) ? File.ReadAllText(Fichero).Trim().ToLowerInvariant() : null; } catch { return null; }
        }

        // Lo elegido por el usuario (es / en): vale para las siguientes veces aunque el ini diga idioma=auto
        public static void Elegir(string v)
        {
            Poner(v);
            try { File.WriteAllText(Fichero, ingles ? "en" : "es"); } catch { }
            try { if (File.Exists(Paths.IniFile)) Ce.IniPoner("idioma", ingles ? "en" : "es"); } catch { }
        }

        public static string T(string s)
        {
            // (20-11) "boton de captura" -> el boton de la generacion de este mando (src\BotonesCe.cs)
            if (!ingles || string.IsNullOrEmpty(s)) return BotonesCe.EnTexto(s, false);
            Cargar();
            string r;
            if (exactas.TryGetValue(s, out r)) return BotonesCe.EnTexto(r, true);
            string o = s;
            foreach (string[] p in tabla) if (o.IndexOf(p[0], StringComparison.Ordinal) >= 0) o = o.Replace(p[0], p[1]);
            return BotonesCe.EnTexto(o, true);
        }

        static void Cargar()
        {
            if (tabla != null) return;
            lock (sync)
            {
                if (tabla != null) return;
                List<string[]> l = new List<string[]>();
                Dictionary<string, string> ex = new Dictionary<string, string>();
                try
                {
                    string f = Path.Combine(Paths.AppDir, @"languages\en.txt");
                    if (File.Exists(f))
                        foreach (string raw in File.ReadAllLines(f, Encoding.UTF8))
                        {
                            if (raw.Length == 0 || raw.StartsWith("#")) continue;
                            int t = raw.IndexOf('\t');
                            if (t <= 0) continue;
                            string es = raw.Substring(0, t), en = raw.Substring(t + 1);
                            ex[es] = en;
                            if (es.Length >= 4) l.Add(new string[] { es, en });
                        }
                }
                catch { }
                l.Sort(delegate(string[] a, string[] b) { return b[0].Length.CompareTo(a[0].Length); });
                exactas = ex;
                tabla = l;
            }
        }
    }
}

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        // Menu de la bandeja: el asistente otra vez (se abre la app de nuevo con --asistente y esta se cierra)
        void AbrirAsistente()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetEntryAssembly().Location;
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(exe, "--asistente --esperar-salida");
                psi.UseShellExecute = false;
                System.Diagnostics.Process.Start(psi);
                Log.Write("Asistente pedido desde la bandeja: la app se cierra y se abre con --asistente");
                QuitRequested();
            }
            catch (Exception ex) { Log.Error("Abriendo el asistente", ex); }
        }
    }
}
