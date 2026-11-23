// Hardware.cs - toda la deteccion de una vez y su informe (ConsoleEnhancer)
//
// Uso (la app, el asistente del primer arranque o el exe de prueba):
//   OpcionesDeteccion op = new OpcionesDeteccion();          // sin abrir la capturadora
//   Hardware hw = Hardware.Detectar(op);
//   PlanImagen plan = Planificador.Elegir(hw, new PreferenciasPlan());
//   Console.WriteLine(hw.Informe()); Console.WriteLine(plan.Informe());
// Nada de esto escribe en el sistema (ni perfiles de NVIDIA, ni registro, ni ficheros). Lo que falla se apunta en Errores y se sigue.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ConsoleEnhancer.Deteccion
{
    public delegate bool PermisoAbrir(string dispositivo);
    public delegate void AvisoAbrir(string dispositivo);

    public sealed class OpcionesDeteccion
    {
        public bool UsarNvidia = true;            // false = como un PC sin NVIDIA (simulacion): fuera NVAPI y las graficas de NVIDIA
        public bool ProbarD3D12 = true;           // dispositivo D3D12 de un momento (nivel de funciones, shader model)
        public bool BuscarCapturadoras = true;    // false = como un PC sin capturadora (simulacion)
        public bool UsarMediaFoundation = true;
        public bool AbrirCapturadoras = false;    // formatos: ABRE el dispositivo (asistente, o con el turno de la capturadora)
        public PermisoAbrir AntesDeAbrir;         // null = abrir sin preguntar
        public AvisoAbrir DespuesDeAbrir;
        public int TopeMs = 8000;
        // Extras no oficiales que solo se usan si el usuario los tiene (no van en el paquete)
        public string[] RutasDlssNr = new string[0];

        // Rutas de serie de los extras: junto a Magpie de la app y en la carpeta de datos
        public static string[] RutasExtra(string appDir, string archivo)
        {
            string datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConsoleEnhancer");
            return new string[] { Path.Combine(appDir, "magpie", archivo), Path.Combine(appDir, "extras", archivo), Path.Combine(datos, "extras", archivo) };
        }
    }

    public sealed class Hardware
    {
        public List<GpuInfo> Gpus = new List<GpuInfo>();
        public List<PantallaInfo> Pantallas = new List<PantallaInfo>();
        public List<CapturadoraInfo> Capturadoras = new List<CapturadoraInfo>();
        public List<AudioEntrada> Audios = new List<AudioEntrada>();
        public List<MandoInfo> Mandos = new List<MandoInfo>();
        public List<string> ProgramasMandos = new List<string>();
        public SistemaInfo Sistema = new SistemaInfo();
        public List<string> Errores = new List<string>();
        public List<string> Tiempos = new List<string>();
        public string Simulacion = "";

        public static Hardware Detectar(OpcionesDeteccion op)
        {
            Hardware h = new Hardware();
            Stopwatch sw = Stopwatch.StartNew();
            Paso(h, "sistema", sw, delegate { h.Sistema = DetectorSistema.Detectar(h.Errores); });
            Paso(h, "gpu", sw, delegate { h.Gpus = DetectorGpu.Detectar(op, h.Errores); });
            Paso(h, "pantallas", sw, delegate { h.Pantallas = DetectorPantallas.Detectar(op, h.Errores); });
            if (op.BuscarCapturadoras)
                Paso(h, op.AbrirCapturadoras ? "capturadoras (abiertas)" : "capturadoras", sw, delegate { h.Capturadoras = DetectorCapturadoras.Detectar(op, h.Audios, h.Errores); });
            Paso(h, "mandos", sw, delegate { h.Mandos = DetectorMandos.Detectar(op, h.ProgramasMandos, h.Errores); });
            return h;
        }

        delegate void Accion();
        static void Paso(Hardware h, string nombre, Stopwatch sw, Accion a)
        {
            long t0 = sw.ElapsedMilliseconds;
            try { a(); }
            catch (Exception ex) { h.Errores.Add(nombre + ": " + ex.GetType().Name + ": " + ex.Message); }
            h.Tiempos.Add(nombre + " " + (sw.ElapsedMilliseconds - t0) + " ms");
        }

        // La grafica que pinta en esa pantalla (por su LUID) o, si no, la de mas VRAM que no sea por software
        public GpuInfo GpuDe(PantallaInfo p)
        {
            if (p != null)
            {
                GpuInfo g = Gpus.Find(delegate(GpuInfo x) { return x.Luid == p.GpuLuid && !x.Software; });
                // (1.2.02) portatil con dos graficas (Optimus) o pantalla en la placa base: la pantalla va por la integrada pero la imagen
                // la hace la dedicada (Windows copia entre graficas); antes salia el plan de la integrada (sin DLSS ni Reflex)
                if (g != null && g.Integrada && Gpus.Exists(delegate(GpuInfo x) { return !x.Software && !x.Integrada; })) g = null;
                if (g != null) return g;
            }
            GpuInfo best = null;
            foreach (GpuInfo g in Gpus)
            {
                if (g.Software) continue;
                if (best == null || (g.Integrada ? 0 : 1) > (best.Integrada ? 0 : 1) || ((g.Integrada == best.Integrada) && g.Vram > best.Vram)) best = g;
            }
            if (best == null && Gpus.Count > 0) best = Gpus[0];
            return best;
        }

        // ------------------------------------------------------------------ informe
        public string Informe()
        {
            StringBuilder sb = new StringBuilder();
            if (Simulacion.Length > 0) sb.AppendLine(Tx.T("*** SIMULACIÓN: ", "*** SIMULATION: ") + Simulacion + " ***");
            SistemaInfo s = Sistema;
            sb.AppendLine("== " + Tx.T("Sistema", "System") + " ==");
            sb.AppendLine("  " + s.Windows + (s.Admin ? Tx.T(", administrador", ", administrator") : ""));
            sb.AppendLine("  CPU: " + s.Cpu + " - " + s.Nucleos + Tx.T(" núcleos / ", " cores / ") + s.Hilos + Tx.T(" hilos", " threads") +
                          (s.NucleosE > 0 ? " (" + s.NucleosP + " P + " + s.NucleosE + " E)" : ""));
            sb.AppendLine("  RAM: " + Tx.Mb(s.Ram) + Tx.T(" (libre ", " (free ") + Tx.Mb(s.RamLibre) + ")" + (s.Portatil ? Tx.T(", portátil (con batería)", ", laptop") : Tx.T(", sobremesa", ", desktop")) +
                          (s.PlanEnergia.Length > 0 ? Tx.T(", plan de energía «", ", power plan \"") + s.PlanEnergia + Tx.T("»", "\"") : ""));
            sb.AppendLine("  " + Tx.T("Juegos en ventana: optimización ", "Windowed games: optimization ") + Tx.SiNo(s.OptimizacionVentana) + ", VRR " + Tx.SiNo(s.VrrVentana) + Tx.T("; idioma ", "; language ") + s.Idioma);

            sb.AppendLine("== " + Tx.T("Gráficas", "GPUs") + " (" + Gpus.Count + ") ==");
            foreach (GpuInfo g in Gpus)
            {
                sb.AppendLine("  " + g.Nombre + (g.Simulada ? Tx.T(" [simulada]", " [simulated]") : ""));
                sb.AppendLine("    " + g.FabTexto + " " + g.VendorId.ToString("X4") + ":" + g.DeviceId.ToString("X4") + Tx.T(", arquitectura ", ", architecture ") + (g.Arquitectura.Length > 0 ? g.Arquitectura : "?") +
                              (g.NvArq > 0 ? " (NVAPI 0x" + g.NvArq.ToString("X") + ")" : "") + Tx.T(", familia ", ", family ") + g.Familia + (g.Portatil ? Tx.T(", portátil", ", laptop") : "") + (g.Integrada ? Tx.T(", integrada", ", integrated") : ""));
                sb.AppendLine("    VRAM " + Tx.Mb(g.Vram) + (g.PresupuestoVram > 0 ? Tx.T(" (usable ahora ", " (budget ") + Tx.Mb(g.PresupuestoVram) + ")" : "") + ", LUID 0x" + g.Luid.ToString("X") +
                              Tx.T(", pantallas ", ", outputs ") + g.Salidas);
                sb.AppendLine("    Driver " + g.Driver + (g.RamaDriver.Length > 0 ? " (" + g.RamaDriver + ")" : "") + (g.DriverUmd.Length > 0 && g.DriverUmd != g.Driver ? " [" + g.DriverUmd + "]" : "") +
                              (g.FechaDriver.Length > 0 ? ", " + g.FechaDriver : "") + ", WDDM " + (g.Wddm > 0 ? (g.Wddm / 1000) + "." + (g.Wddm % 1000 / 100) : "?") +
                              ", HAGS " + (g.Hags == 2 ? Tx.T("encendida", "on") : g.Hags == 1 ? Tx.T("apagada", "off") : g.Hags == 0 ? Tx.T("sin soporte", "unsupported") : "?") +
                              (g.NivelD3D12 > 0 ? ", D3D12 " + DetectorGpu.NivelTexto(g.NivelD3D12) + ", SM " + (g.ShaderModel >> 4) + "." + (g.ShaderModel & 0xF) : "") +
                              Tx.T(", índice ~", ", index ~") + Tx.N(g.Indice, "0.00") + (g.IndiceEstimado ? "?" : ""));
                if (g.Software) continue;
                GpuFunciones f = g.F;
                sb.AppendLine("    DLSS SR: " + f.DlssSr + " | transformer: " + f.DlssTransformer.ToString());
                sb.AppendLine("    Smooth Motion: " + f.SmoothMotion);
                sb.AppendLine("    Reflex: " + f.Reflex + " | NVOF: " + f.Nvof);
                sb.AppendLine("    DLSS 5 (DLSSNR): " + f.DlssNr);
                sb.AppendLine("    RTX Video: " + f.RtxVideo + " | RTX HDR: " + f.RtxHdr);
                sb.AppendLine("    FSR 1: " + f.Fsr1 + " | FSR 3 FG: " + f.Fsr3Fg + " | FSR 4: " + f.Fsr4);
                sb.AppendLine("    XeSS: " + f.XessSr + " | XeSS FG: " + f.XessFg);
                sb.AppendLine("    AFMF: " + f.Afmf + " | Anti-Lag: " + f.AntiLag + " | " + Tx.T("motor propio: ", "own engine: ") + f.Motor);
            }

            sb.AppendLine("== " + Tx.T("Pantallas", "Displays") + " (" + Pantallas.Count + ") ==");
            foreach (PantallaInfo p in Pantallas)
            {
                sb.AppendLine("  " + p.Nombre + " (" + p.Gdi + (p.Pnp.Length > 0 ? ", " + p.Pnp : "") + ", " + p.Conexion + (p.Principal ? Tx.T(", principal", ", primary") : "") + ")" + (p.Simulada ? Tx.T(" [simulada]", " [simulated]") : ""));
                sb.AppendLine("    " + Tx.T("Ahora ", "Now ") + p.Ancho + "x" + p.Alto + " @ " + Tx.N(p.HzExacto, "0.000") + " Hz" + (p.Es119 ? Tx.T(" (119,88: ojo con Smooth Motion)", " (119.88)") : "") +
                              Tx.T("; máximo ", "; max ") + p.AnchoMax + "x" + p.AltoMax + " @ " + p.HzEnMax + " Hz (" + string.Join("/", p.HzDeLaMax.ConvertAll<string>(delegate(int x) { return x.ToString(); }).ToArray()) + ")" +
                              Tx.T(", Hz máximos ", ", max Hz ") + p.HzMax + (p.AnchoNativo > 0 ? Tx.T(", nativa ", ", native ") + p.AnchoNativo + "x" + p.AltoNativo : ""));
                sb.AppendLine("    HDR " + (p.HdrActivo ? Tx.T("encendido", "on") : p.HdrPosible ? Tx.T("posible (apagado)", "capable (off)") : "no") + (p.BitsColor > 0 ? ", " + p.BitsColor + " bits" : "") +
                              (p.NitsMax > 0 ? ", " + Tx.N(p.NitsMax, "0") + " nits" : "") + (p.Hdr10Edid ? ", EDID HDR10" : "") + (p.HlgEdid ? " HLG" : "") +
                              "; VRR " + (p.VrrMax > 0 ? "HDMI " + p.VrrMin + "-" + p.VrrMax + " Hz" : "") + (p.FreeSyncMax > 0 ? " FreeSync " + p.FreeSyncMin + "-" + p.FreeSyncMax + " Hz" : "") +
                              (p.VrrNvidia >= 0 ? " G-SYNC " + ((p.VrrNvidia & 2) != 0 ? Tx.T("posible", "possible") : "no") + ((p.VrrNvidia & 1) != 0 ? Tx.T(" y activo", " and active") : "") : "") +
                              (!p.VrrPosible && p.VrrNvidia < 0 ? "no" : "") + (p.Allm ? ", ALLM" : "") + (p.Frl > 0 ? ", HDMI 2.1 FRL " + p.Frl : "") + ", tearing " + Tx.SiNo(p.Tearing) +
                              (p.RangoMax > 0 ? Tx.T(", rango ", ", range ") + p.RangoMin + "-" + p.RangoMax + " Hz" : ""));
            }

            sb.AppendLine("== " + Tx.T("Capturadoras", "Capture devices") + " (" + Capturadoras.Count + ") ==");
            if (Capturadoras.Count == 0) sb.AppendLine(Tx.T("  ninguna: conecta una capturadora (o instala su driver)", "  none: plug in a capture device"));
            foreach (CapturadoraInfo c in Capturadoras)
            {
                sb.AppendLine("  " + c.Nombre + " [" + Tipo(c.Tipo) + ", " + c.Puntos + Tx.T(" puntos", " pts") + "]" + (c.Simulada ? Tx.T(" [simulada]", " [simulated]") : ""));
                sb.AppendLine("    " + c.Bus + (c.Vid != 0 ? " " + c.Vid.ToString("X4") + ":" + c.Pid.ToString("X4") : "") + (c.NombrePnp.Length > 0 ? Tx.T(", Windows: «", ", Windows: \"") + c.NombrePnp + Tx.T("»", "\"") : "") +
                              (c.Fabricante.Length > 0 ? ", " + c.Fabricante : "") + (c.Servicio.Length > 0 ? ", driver " + c.Servicio : "") +
                              ", DirectShow " + Tx.SiNo(c.EnDirectShow) + ", Media Foundation " + Tx.SiNo(c.EnMediaFoundation));
                if (c.PorQue.Length > 0) sb.AppendLine("    " + Tx.T("por qué: ", "why: ") + c.PorQue);
                if (c.Modelo != null) sb.AppendLine("    " + Tx.T("catálogo: ", "catalog: ") + c.Modelo.Marca + " " + c.Modelo.Modelo + (c.Modelo.Formatos.Length > 0 ? " - " + c.Modelo.Formatos : "") + (c.Modelo.Notas.Length > 0 ? " (" + c.Modelo.Notas + ")" : ""));
                sb.AppendLine("    Audio: " + (c.Audio != null ? c.Audio.Nombre + " [" + c.Audio.Origen + "] - " : "") + c.AudioMotivo);
                if (c.Formatos != null)
                {
                    sb.AppendLine("    " + Tx.T("Formatos (abierta): ", "Formats (opened): ") + c.Formatos.Count + (c.PinesAudio >= 0 ? Tx.T(", pines de audio en el filtro: ", ", audio pins on the filter: ") + c.PinesAudio : ""));
                    List<string> fl = new List<string>();
                    foreach (FormatoCaptura f in c.Formatos) fl.Add(f.ToString());
                    for (int i = 0; i < fl.Count; i += 4) sb.AppendLine("      " + string.Join(" | ", fl.GetRange(i, Math.Min(4, fl.Count - i)).ToArray()));
                }
                else if (c.FormatosError.Length > 0) sb.AppendLine("    " + Tx.T("Formatos: ", "Formats: ") + c.FormatosError);
                else sb.AppendLine("    " + Tx.T("Formatos: sin abrir (los da el asistente al elegirla)", "Formats: not opened (the wizard reads them)"));
            }
            List<string> au = new List<string>();
            foreach (AudioEntrada a in Audios) au.Add(a.Nombre + " [" + a.Origen + "]");
            sb.AppendLine("  " + Tx.T("Entradas de audio: ", "Audio inputs: ") + (au.Count == 0 ? Tx.T("ninguna", "none") : string.Join("; ", au.ToArray())));

            sb.AppendLine("== " + Tx.T("Mandos", "Controllers") + " (" + Mandos.Count + ") ==");
            foreach (MandoInfo m in Mandos)
                sb.AppendLine("  " + m.Nombre + " [" + m.Origen + (m.Ranura >= 0 ? " " + (m.Ranura + 1) : "") + "] " + (m.Vid != 0 ? m.Vid.ToString("X4") + ":" + m.Pid.ToString("X4") + " " : "") +
                              (m.Marca.Length > 0 ? m.Marca + " " : "") + m.Tipo + (m.Conexion.Length > 0 ? ", " + m.Conexion : "") + (m.Nota.Length > 0 ? " - " + m.Nota : ""));
            if (Mandos.Count == 0) sb.AppendLine(Tx.T("  ninguno conectado", "  none connected"));
            if (ProgramasMandos.Count > 0) sb.AppendLine("  " + Tx.T("Programas de mandos abiertos/instalados: ", "Controller software: ") + string.Join(", ", ProgramasMandos.ToArray()));

            if (Errores.Count > 0)
            {
                sb.AppendLine("== " + Tx.T("Avisos de la detección", "Detection notes") + " ==");
                foreach (string e in Errores) sb.AppendLine("  " + e);
            }
            sb.AppendLine("  (" + string.Join(", ", Tiempos.ToArray()) + ")");
            return sb.ToString();
        }

        public static string Tipo(TipoCaptura t)
        {
            switch (t)
            {
                case TipoCaptura.Capturadora: return Tx.T("capturadora", "capture card");
                case TipoCaptura.Webcam: return "webcam";
                case TipoCaptura.Virtual: return Tx.T("virtual", "virtual");
                default: return Tx.T("¿capturadora?", "capture?");
            }
        }
    }
}
