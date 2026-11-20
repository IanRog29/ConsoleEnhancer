// ConsoleEnhancerApp.cs - lo propio de ConsoleEnhancer en el lanzador (fase 2, agente UNIVERSAL, 18-11-2026)
//
// ConsoleEnhancer = Switch2Magpie + Switch2Engine + Magpie + mpv para cualquier consola, capturadora y PC (C:\ConsoleEnhancer\DISENO.md).
// Aqui:
//  - Claves nuevas del ini (Config es partial): device_path, audio_dispositivo, audio_pines, gpu_fabricante, motor_streamline,
//    motor_adaptador, generacion_disponible, idioma
//  - Ce.Arranque: antes de leer el ini. Sin ini (primer arranque), con --asistente o si el hardware ha cambiado (huella en
//    datos\hardware.txt): la deteccion (src\Deteccion) y el asistente (Asistente.cs). --asistente-auto (y --dry-run sin ini): el plan
//    de la deteccion sin preguntar nada
//  - Ce.EscribirIni: recursos\base.ini (ajustes generales probados) + el bloque del plan (lo que depende del hardware) -> el ini
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
            if (CeParseInicio(key, val)) return true; // (iniciar_con_windows, apagar_pc_aviso_s: src\InicioWindows.cs)
            if (BotonesCe.Parse(key, val)) return true; // (20-11: mando_botones, src\BotonesCe.cs)
            if (CeParseImagen(key, val)) return true;   // (20-11: hdr, rtx_hdr*, vrr, nvidia_latencia..., src\ImagenCe.cs)
            if (CeParseEscalas(key, val)) return true;  // (20-11: escalas_adaptables..., src\Escalas.cs)
            switch (key)
            {
                case "device_path": DevicePath = val.Trim(); return true;
                case "audio_dispositivo": AudioDevice = val.Trim(); return true;
                case "audio_pines": AudioPins = val.Trim() == "1"; return true;
                case "gpu_fabricante": GpuVendor = val.Trim().ToLowerInvariant(); Ce.NoNvidia = GpuVendor.Length > 0 && GpuVendor != "nvidia"; return true;
                case "motor_streamline": EngineStreamline = val.Trim().ToLowerInvariant(); return true;
                case "motor_adaptador": EngineAdapter = val.Trim(); return true;
                case "generacion_disponible": FgAvailable = val.Trim() != "0"; if (!FgAvailable) FgOff = true; return true;
                case "idioma": Language = val.Trim().ToLowerInvariant(); Idioma.Poner(Language); return true;
            }
            return false;
        }
        // (FgMagpieOnly: desde el 19-11 lo trae Prisma, Switch2Magpie.cs, GENERACION)
    }

    static partial class Ce
    {
        // Sin NVIDIA: nada de NVAPI (perfiles, Smooth Motion); lo pone gpu_fabricante del ini
        public static volatile bool NoNvidia;

        public static string BaseIni { get { return Path.Combine(Paths.AppDir, @"recursos\base.ini"); } }
        public static string HardwareFile { get { return Path.Combine(Paths.Datos, "hardware.txt"); } }
        public static string EngineLogDir { get { return Path.Combine(Paths.Datos, "logs"); } }

        // ------------------------------------------------------------------ el motor
        // El escalador del motor: DLSS si lo hay; si no (o el alternativo de R3), FSR 1 (EASU + RCAS, shader propio del motor)
        public static string EngineUpscaler(Config cfg)
        {
            string s = cfg.ScalerNow;
            if (s == "dlss") return NoNvidia ? "fsr1" : "dlss";
            if (s == "fsr" || s == "fsr2" || s == "fsr3" || s == "fsr4" || s == "xess") return "fsr1";
            return "fsr1";   // (R3 / sin DLSS: el FSR 1 propio del motor; catrom queda en el motor por si se pide a mano)
        }

        public static string[] EngineArgsExtra(Config cfg)
        {
            List<string> a = new List<string>();
            if (cfg.DevicePath.Length > 0 && cfg.TestSource.Length == 0) a.Add("device_path=" + cfg.DevicePath);
            a.Add("streamline=" + (NoNvidia ? "0" : cfg.EngineStreamline));
            if (cfg.EngineAdapter.Length > 0) a.Add("adapter_luid=" + cfg.EngineAdapter);
            a.Add("datos=" + EngineLogDir);
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
            if (!nuevo && !forzar && !auto)
            {
                // cambio de hardware: la huella de la ultima deteccion
                string vieja = LeerHuella();
                if (vieja == null) return -1;
                Hardware rapido = Detectar(false);
                string huella = Huella(rapido);
                if (huella == vieja) return -1;
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
                if (Directory.Exists(ex) && Directory.Exists(Paths.MagpieDir) && !Directory.Exists(dst))
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

        // ------------------------------------------------------------------ el plan -> el ini
        // base.ini (lo general) + el bloque del plan (lo de este hardware; va al final: en el ini manda la ultima linea de cada clave).
        // audioElegido: null = el del plan
        // inicio / apagar: lo elegido en el ultimo paso del asistente (iniciar_con_windows, apagar_pc); null = lo que hubiera en el ini
        public static void EscribirIni(PlanImagen plan, Hardware hw, string consola, string idioma, AudioEntrada audioElegido, bool? inicio = null, bool? apagar = null, bool? escritorio = null, OpcionesImagen imagen = null)
        {
            string iniAntes = IniLeer("iniciar_con_windows"), apagarAntes = IniLeer("apagar_pc"), avisoAntes = IniLeer("apagar_pc_aviso_s"), escAntes = IniLeer("escritorio_negro");
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
            sb.AppendLine("escritorio_negro=" + (escritorio.HasValue ? (escritorio.Value ? "1" : "0") : escAntes == "1" ? "1" : "0"));
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.IniFile));
            if (File.Exists(Paths.IniFile))
            {
                try { File.Copy(Paths.IniFile, Paths.IniFile + ".anterior", true); } catch { }
            }
            File.WriteAllText(Paths.IniFile, sb.ToString(), new UTF8Encoding(false));
            // generacion de serie (el boton de captura la cambia luego: juegos\generacion.txt)
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
            sb.AppendLine("escalador=" + (p.Escalador == "dlss" ? "dlss" : p.Escalador == "ninguno" ? (nv && g != null && g.F.DlssSr.Si ? "dlss" : "fsr") : "fsr"));
            string alt = nv && g != null && g.F.RtxVideo.Si ? "vsr" : g != null && g.F.XessSr.Si && g.Fab == Fabricante.Intel ? "xess" : "lanczos";
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
            else if (magpieFg || altFsr3)
            {
                string t = g60 == "xessfg" || g30 == "xessfg" ? "xess" : "fsr3";
                sb.AppendLine("generacion_tipo=" + t);
                sb.AppendLine("generacion_tipo_60=" + t);
                sb.AppendLine("generacion_tipo_30=" + t);
                sb.AppendLine("generacion_disponible=1");
            }
            else sb.AppendLine("generacion_disponible=0");
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
    // idiomas\en.txt: "texto en espanol<TAB>english text" por linea. Primero la frase entera; si no esta, trozos conocidos (los mas
    // largos antes). Lo que no este en la tabla sale en espanol
    static class Idioma
    {
        static volatile bool ingles;
        static List<string[]> tabla;
        static Dictionary<string, string> exactas;
        static readonly object sync = new object();

        public static bool Ingles { get { return ingles; } }

        public static void Poner(string v)
        {
            bool en = v == "en" || (v != "es" && !IdiomaWindowsEs());
            ingles = en;
            Tx.Ingles = en;
        }

        static bool IdiomaWindowsEs()
        {
            try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es"; } catch { return true; }
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
                    string f = Path.Combine(Paths.AppDir, @"idiomas\en.txt");
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
