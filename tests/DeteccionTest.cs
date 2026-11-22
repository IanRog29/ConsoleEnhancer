// DeteccionTest.cs - prueba del modulo de deteccion de ConsoleEnhancer: imprime lo detectado en este PC y el plan elegido, y simula
// otros PCs (sin NVIDIA, sin capturadora, AMD, Intel, RTX 20/30/50, GTX, capturadoras UVC, pantallas de 60/240 Hz...).
//
// Compilar: tests\build_deteccion.bat  ->  tests\bin\DeteccionTest.exe
// Uso:
//   DeteccionTest.exe                     este PC (sin abrir la capturadora) + los casos "sin-nvidia" y "sin-capturadora"
//   DeteccionTest.exe --formatos          ademas abre la capturadora para leer sus formatos (con el turno de la capturadora)
//   DeteccionTest.exe --simula=todo       todos los casos simulados (o --simula=amd-rx7800xt, ...)
//   DeteccionTest.exe --ingles  --ini  --consola=switch2  --sin-d3d12  --salida=<fichero>  --turno=<gpu.lock>  --magpie=<carpeta>
// Codigo de salida: 0 si todo ha ido sin excepciones (los avisos de la deteccion no cuentan), 1 si algo ha reventado.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using ConsoleEnhancer.Deteccion;

static class DeteccionTest
{
    // Fichero de turno de la capturadora (con --formatos y varios procesos a la vez): --turno=<fichero> o la variable CE_TURNO; sin el, nada
    static string turno = Environment.GetEnvironmentVariable("CE_TURNO") ?? "";
    static StringBuilder salida = new StringBuilder();
    static int fallos;

    static void W(string s) { Console.WriteLine(s); salida.AppendLine(s); }

    [MTAThread]
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        bool formatos = false, ini = false, d3d12 = true;
        string simula = "", consola = "", fichero = "", magpie = "";
        foreach (string a in args)
        {
            if (a == "--formatos") formatos = true;
            else if (a == "--ingles") Tx.Ingles = true;
            else if (a == "--ini") ini = true;
            else if (a == "--sin-d3d12") d3d12 = false;
            else if (a.StartsWith("--simula=")) simula = a.Substring(9);
            else if (a.StartsWith("--consola=")) consola = a.Substring(10);
            else if (a.StartsWith("--salida=")) fichero = a.Substring(9);
            else if (a.StartsWith("--turno=")) turno = a.Substring(8);
            else if (a.StartsWith("--magpie=")) magpie = a.Substring(9);
        }

        // Extras no oficiales: los busca donde los tendria el usuario (la raiz del repositorio, su extras\, terceros\magpie y la carpeta
        // de Magpie que diga --magpie=<carpeta>)
        string ce = AppDomain.CurrentDomain.BaseDirectory;
        string raiz = Path.GetFullPath(Path.Combine(ce, @"..\.."));
        OpcionesDeteccion op = new OpcionesDeteccion();
        op.ProbarD3D12 = d3d12;
        List<string> dnr = new List<string>(OpcionesDeteccion.RutasExtra(raiz, "nvngx_dlssnr.dll"));
        List<string> sm86 = new List<string>(OpcionesDeteccion.RutasExtra(raiz, @"runtimes\dlssg_sm86"));
        dnr.Add(Path.Combine(raiz, @"terceros\magpie\nvngx_dlssnr.dll"));
        sm86.Add(Path.Combine(raiz, @"terceros\magpie\runtimes\dlssg_sm86"));
        if (magpie.Length > 0) { dnr.Add(Path.Combine(magpie, "nvngx_dlssnr.dll")); sm86.Add(Path.Combine(magpie, @"runtimes\dlssg_sm86")); }
        op.RutasDlssNr = dnr.ToArray();
        op.RutasDlssgSm86 = sm86.ToArray();
        op.AbrirCapturadoras = formatos;
        op.AntesDeAbrir = CogerTurno;
        op.DespuesDeAbrir = SoltarTurno;

        PreferenciasPlan pref = new PreferenciasPlan();
        pref.Consola = consola;

        if (simula.Length == 0 || simula == "aqui")
        {
            W("############ " + Tx.T("ESTE PC", "THIS PC") + " ############");
            Caso(null, op, pref, ini, true);
        }
        if (simula == "matriz" || simula.StartsWith("matriz:"))
        {
            Matriz(op, pref, simula.Length > 7 ? simula.Substring(7) : "");
            if (fichero.Length > 0) { try { File.WriteAllText(fichero, salida.ToString(), new UTF8Encoding(false)); } catch (Exception ex) { Console.WriteLine("salida: " + ex.Message); } }
            return fallos == 0 ? 0 : 1;
        }
        List<string> casos = new List<string>();
        if (simula == "todo") casos.AddRange(Simulador.Casos);
        else if (simula.Length > 0 && simula != "aqui") casos.AddRange(simula.Split(','));
        else if (simula.Length == 0) { casos.Add("sin-nvidia"); casos.Add("sin-capturadora"); }
        foreach (string c in casos)
        {
            W("");
            W("############ " + Tx.T("SIMULADO: ", "SIMULATED: ") + c + " ############");
            OpcionesDeteccion o = Simulador.Opciones(c, op);
            o.AbrirCapturadoras = false;   // las simulaciones no abren nada
            Caso(c, o, pref, ini, c == "sin-nvidia" || c == "sin-capturadora" || simula.Length > 0 && simula != "todo");
        }
        if (fichero.Length > 0) { try { File.WriteAllText(fichero, salida.ToString(), new UTF8Encoding(false)); } catch (Exception ex) { Console.WriteLine("salida: " + ex.Message); } }
        W("");
        W(fallos == 0 ? Tx.T("Fin: sin excepciones.", "Done: no exceptions.") : Tx.T("Fin: ", "Done: ") + fallos + Tx.T(" excepciones.", " exceptions."));
        return fallos == 0 ? 0 : 1;
    }

    static void Caso(string caso, OpcionesDeteccion op, PreferenciasPlan pref, bool ini, bool completo)
    {
        Hardware hw = null;
        try
        {
            hw = Hardware.Detectar(op);
            if (caso != null) Simulador.Aplicar(hw, caso, op);
        }
        catch (Exception ex) { fallos++; W("EXCEPCION en la deteccion: " + ex); return; }
        try
        {
            if (completo) W(hw.Informe());
            else
            {
                // resumen: la grafica, la pantalla y la capturadora que cambian en el caso
                foreach (GpuInfo g in hw.Gpus)
                    W("  GPU " + g.Nombre + " [" + g.Familia + ", " + Tx.Mb(g.Vram) + ", driver " + g.Driver + "]: DLSS " + Tx.SiNo(g.F.DlssSr.Si) + ", DLSS-G " + Tx.SiNo(g.F.DlssG.Si) +
                      (g.F.MfgMax > 2 ? " x" + g.F.MfgMax : "") + ", Smooth Motion " + Tx.SiNo(g.F.SmoothMotion.Si) + ", Reflex " + Tx.SiNo(g.F.Reflex.Si) + ", FSR 3 FG " + Tx.SiNo(g.F.Fsr3Fg.Si) +
                      ", XeSS FG " + Tx.SiNo(g.F.XessFg.Si) + ", AFMF " + Tx.SiNo(g.F.Afmf.Si) + ", FSR 4 " + Tx.SiNo(g.F.Fsr4.Si) + ", DLSS 5 " + Tx.SiNo(g.F.DlssNr.Si) +
                      (g.F.DlssG.Si ? "" : " (DLSS-G: " + g.F.DlssG.Motivo + ")"));
                foreach (PantallaInfo p in hw.Pantallas) if (p.Principal) W("  " + Tx.T("Pantalla ", "Display ") + p.Nombre + " " + p.Ancho + "x" + p.Alto + " @ " + Tx.N(p.HzExacto, "0.###") + " Hz, VRR " + Tx.SiNo(p.VrrPosible) + ", HDR " + Tx.SiNo(p.HdrPosible));
                foreach (CapturadoraInfo c in hw.Capturadoras) if (c.Tipo != TipoCaptura.Virtual) W("  " + Tx.T("Capturadora ", "Capture ") + c.Nombre + " [" + Hardware.Tipo(c.Tipo) + "] audio: " + (c.Audio != null ? c.Audio.Nombre + " (" + c.AudioMotivo + ")" : c.AudioMotivo));
                if (hw.Capturadoras.Count == 0) W("  " + Tx.T("Capturadoras: ninguna", "Capture devices: none"));
            }
            PlanImagen plan = Planificador.Elegir(hw, pref);
            W(plan.Informe());
            if (ini) W(plan.ComoIni());
        }
        catch (Exception ex) { fallos++; W("EXCEPCION en el plan: " + ex); }
    }

    // ---- (1.2.2, UNIVERSAL) la matriz: graficas x pantallas x capturadoras (todas las del catalogo con clave + las UVC simuladas), hardware
    // inventado entero y la revision de cada plan (Simulador.Revisar). Una linea por combinacion; "MAL:" = algo que arreglar.
    // --simula=matriz  o  --simula=matriz:<extra>  (p. ej. matriz:cpu-4hilos)
    static void Matriz(OpcionesDeteccion op, PreferenciasPlan pref, string extra)
    {
        List<string> caps = new List<string>();
        foreach (ModeloCaptura m in DetectorCapturadoras.Catalogo) if (m.Clave.Length > 0 && m.Modos.Length > 0) caps.Add("cap:" + m.Clave);
        caps.Add("uvc-ms2109"); caps.Add("uvc-ms2130");
        int n = 0, mal = 0;
        Dictionary<string, int> porMotivo = new Dictionary<string, int>();
        foreach (string gpu in Simulador.MatrizGpus)
            foreach (string pan in Simulador.MatrizPantallas)
                foreach (string cap in caps)
                {
                    n++;
                    try
                    {
                        Hardware hw = Simulador.Sintetico(op, gpu, pan, cap, extra);
                        PlanImagen p = Planificador.Elegir(hw, pref);
                        List<string> r = Simulador.Revisar(hw, p);
                        string fr; string c120 = p.Captura != null && p.Captura.Formatos != null ? p.Capturas120(out fr) : "";
                        string modos = "";
                        foreach (KeyValuePair<string, ModoPlan> kv in p.Modos)
                            modos += " " + kv.Key + "=" + (kv.Value.Modelo.Length > 0 ? kv.Value.Modelo : p.Escalador) + "@" + Math.Round(100 * kv.Value.CosteMs / ((kv.Key.StartsWith("60") ? 1000.0 : 2000.0) / 59.94)) + "%";
                        // (LIM = solo lo que la grafica no da y el plan ya avisa: integradas a 4K)
                        bool lim = r.Count > 0 && r.TrueForAll(delegate(string x) { return x.Contains("[avisado"); });
                        if (lim) { r.Clear(); }
                        W((r.Count > 0 ? "MAL " : lim ? "LIM " : "ok  ") + gpu + " | " + pan + " | " + cap + " -> " + p.Motor + " " + p.CapW + "x" + p.CapH + " " + p.CapFormato + " " + p.CapFps +
                          " | " + p.Escalador + " | gen " + p.Gen60 + "/" + p.Gen30 + (p.GenDeSerie ? " (serie)" : "") + " | 120: " + (c120.Length > 0 ? c120 : "-") + " |" + modos +
                          (r.Count > 0 ? " || " + string.Join("; ", r.ToArray()) : ""));
                        if (r.Count > 0)
                        {
                            mal++;
                            foreach (string x in r) { string k = x.Split('(')[0].Trim(); int v; porMotivo.TryGetValue(k, out v); porMotivo[k] = v + 1; }
                        }
                    }
                    catch (Exception ex) { fallos++; W("EXCEPCION " + gpu + " | " + pan + " | " + cap + ": " + ex); }
                }
        W("");
        W("Matriz: " + n + " combinaciones, " + mal + " con algo mal");
        foreach (KeyValuePair<string, int> kv in porMotivo) W("  " + kv.Value + " x " + kv.Key);
    }

    // ---- turno de la capturadora (REGLAS.md del coordinador): el fichero gpu.lock; se espera hasta 3 min si lo tiene otro
    static bool CogerTurno(string dispositivo)
    {
        if (string.IsNullOrEmpty(turno)) return true;
        for (int i = 0; i < 90; i++)
        {
            try
            {
                using (FileStream f = new FileStream(turno, FileMode.CreateNew, FileAccess.Write))
                {
                    byte[] b = Encoding.UTF8.GetBytes("UNIVERSAL " + DateTime.Now.ToString("HH:mm:ss") + " (DeteccionTest: formatos de " + dispositivo + ")");
                    f.Write(b, 0, b.Length);
                }
                Console.WriteLine("  [turno cogido para " + dispositivo + "]");
                return true;
            }
            catch (IOException)
            {
                try
                {
                    if (File.Exists(turno) && (DateTime.Now - File.GetLastWriteTime(turno)).TotalMinutes > 20) { File.Delete(turno); continue; }
                }
                catch { }
                if (i == 0) Console.WriteLine("  [turno ocupado: " + Leer(turno) + "; se espera]");
                Thread.Sleep(2000);
            }
            catch (Exception ex) { Console.WriteLine("  [turno: " + ex.Message + "]"); return false; }
        }
        return false;
    }

    static void SoltarTurno(string dispositivo)
    {
        try
        {
            if (File.Exists(turno) && Leer(turno).StartsWith("UNIVERSAL")) { File.Delete(turno); Console.WriteLine("  [turno soltado]"); }
        }
        catch (Exception ex) { Console.WriteLine("  [soltar turno: " + ex.Message + "]"); }
    }

    static string Leer(string f) { try { return File.ReadAllText(f); } catch { return "?"; } }
}
