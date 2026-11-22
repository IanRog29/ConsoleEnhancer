// EscalaPrueba.cs - 21-11-2026 (1.2.1, agente ESCALA): prueba sin consola de la escala de DLSS 5 automatica (src\Lanzador\EscalaAuto.cs).
//   ConsoleEnhancer.exe --test-escala[=<informe>]   (de serie data\games\test_escala.txt; el codigo de salida = fallos)
// 1) El % de DLSS 5 que sale del modelo en las graficas del simulador (src\Deteccion\Simulador.cs: RTX 2060, 3060, 4090; mas la RTX 4060 Ti
//    del usuario) y en cada escenario (1440p 30 + FG -> 4K con E/K/M/L, 1440p 120, 4K 60...), con lo que debe cumplir: mas % con un
//    DLSS SR mas ligero, con menos pasadas, con mejor grafica y en el modo de menos fps; lo medido en Prisma (4060 Ti) +-15 puntos.
// 2) Decisiones (EscalaAuto.Decidir) con medidas inventadas: no llega -> baja (y cuanto), sobra -> sube pendiente, la fuente a menos ->
//    nada, la salida de la generacion, y una secuencia sin vaivenes (sube, no llega, baja, ya no vuelve a subir al que fallo).
// 3) La config de Magpie que se escribiria (MagpieConfig.Chain con una Config en memoria): inputResolutionPercent = el % elegido.
// Nada toca la capturadora, Magpie ni lo aprendido de verdad (data\scales_auto.txt): lo aprendido va a un fichero temporal.
// C# 5
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    static class EscalaPrueba
    {
        static StringBuilder sb;
        static int fallos, pruebas;

        static void Ok(bool cond, string que)
        {
            pruebas++;
            if (!cond) fallos++;
            sb.AppendLine((cond ? "  ok    " : "  FALLO ") + que);
        }

        sealed class G { public string Nombre; public double Indice; public string Fam; public double Vram; }

        // Las graficas del simulador (los mismos datos que Simulador.Gpu) y la del usuario, por DetectorGpu.Derivar (familia e indice de Gpu.cs)
        static G Gpu(string nombre, int dev, double vram, int arq, int drv)
        {
            GpuInfo g = new GpuInfo();
            g.Simulada = true; g.Nombre = nombre; g.VendorId = 0x10DE; g.DeviceId = dev; g.Fab = Fabricante.Nvidia;
            g.Vram = (ulong)(vram * 1024 * 1024 * 1024); g.NvArq = arq; g.Nvapi = true; g.DriverNv = drv; g.Hags = 2; g.Wddm = 3200;
            g.NivelD3D12 = 0xc100; g.ShaderModel = 0x66; g.Salidas = 1;
            DetectorGpu.Derivar(g, new OpcionesDeteccion());
            G r = new G();
            r.Nombre = nombre.Replace("NVIDIA GeForce ", ""); r.Indice = g.Indice; r.Vram = g.VramGb;
            r.Fam = g.Familia == FamiliaGpu.NvidiaRtx20 ? "rtx20" : g.Familia == FamiliaGpu.NvidiaRtx30 ? "rtx30" : g.Familia == FamiliaGpu.NvidiaRtx40 ? "rtx40" :
                    g.Familia == FamiliaGpu.NvidiaRtx50 ? "rtx50" : "otra";
            return r;
        }

        static CadenaEscala Cadena(G g, int modo, string cap, string ven, string gen, string esc, int modelo, bool ligero, int outW, int outH)
        {
            CadenaEscala c = new CadenaEscala();
            c.Modo = modo; c.Objetivo = modo == 120 ? 120 : modo == 30 ? 29.97 : 59.94;
            string[] a = cap.Split('x'), b = ven.Split('x');
            c.CapW = int.Parse(a[0]); c.CapH = int.Parse(a[1]); c.VenW = int.Parse(b[0]); c.VenH = int.Parse(b[1]);
            c.OutW = outW; c.OutH = outH;
            c.Gen = modo >= 100 ? "" : gen; c.Mult = 2;
            c.Esc = esc; c.Escala = !(c.VenW >= outW && c.VenH >= outH); c.Modelo = modelo;
            c.Ligero = ligero; c.FlujoSr = !ligero;
            c.VectoresFg = c.Gen == "dlss" && modo == 30 && c.VenH < 2000;
            c.Indice = g.Indice; c.Familia = g.Fam; c.VramGb = g.Vram;
            return c;
        }

        static int Pct(CadenaEscala c, int n) { return EscalaAuto.Mejor(c, n, 1.0, 0); }
        static string P3(CadenaEscala c) { return Pct(c, 1) + " / " + Pct(c, 2) + " / " + Pct(c, 3); }
        static int R(int p) { return p < 0 ? 0 : p; }   // (-1 = no cabe: lo mas bajo)

        public static int Run(string arg)
        {
            // --test-escala=<informe>[;<magpie.log de una sesion de verdad>]
            string informe = arg, logMagpie = null;
            if (arg != null && arg.IndexOf(';') >= 0) { informe = arg.Substring(0, arg.IndexOf(';')); logMagpie = arg.Substring(arg.IndexOf(';') + 1); }
            if (informe != null && informe.Length == 0) informe = null;
            sb = new StringBuilder();
            fallos = pruebas = 0;
            string tmp = Path.Combine(Path.GetTempPath(), "ce_test_escala_" + DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture) + ".txt");
            EscalaAuto.Reiniciar(tmp);
            double usoAntes = EscalaAuto.Uso;
            // (22-11, 1.2.2: sin el modelo de DLSS elegido en el menu de ajustes de este PC: la prueba pide el suyo; ver PruebasAisladas.cs)
            string modeloAntes = Config.DlssPickSoloPrueba("");
            try
            {
                sb.AppendLine("ConsoleEnhancer " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " - prueba de la escala de DLSS 5 automatica (src\\Lanzador\\EscalaAuto.cs)");
                sb.AppendLine("Uso de la grafica permitido: " + Math.Round(EscalaAuto.Uso * 100) + " % del tiempo de una imagen real. % = 1 / 2 / 3 pasadas (-1 = no cabe ni al minimo)");
                sb.AppendLine();
                G ti = Gpu("NVIDIA GeForce RTX 4060 Ti", 0x2803, 8, 0x190, 61088), r2060 = Gpu("NVIDIA GeForce RTX 2060", 0x1F08, 6, 0x160, 59000),
                  r3060 = Gpu("NVIDIA GeForce RTX 3060", 0x2504, 12, 0x170, 61692), r4090 = Gpu("NVIDIA GeForce RTX 4090", 0x2684, 24, 0x190, 61692);
                G[] gs = { r2060, r3060, ti, r4090 };
                foreach (G g in gs) sb.AppendLine("Grafica " + g.Nombre + ": indice " + g.Indice.ToString("0.##", CultureInfo.InvariantCulture) + ", " + g.Fam + ", " + g.Vram + " GB");
                // (solo informa: el % de grafica de NVAPI en este PC, lo que lee la vigilancia; sin NVIDIA, -1)
                int u1 = Ce.NoNvidia ? -1 : EscalaAuto.UsoAhora();
                sb.AppendLine("NVAPI, % de grafica ahora en este PC: " + u1 + (u1 < 0 ? " (" + EscalaAuto.UsoError + ")" : ""));
                sb.AppendLine();
                Modelo(gs, ti, r2060, r3060, r4090);
                Decisiones(ti);
                ConfigMagpie(ti, r4090);
                ConTuIni();
                if (!string.IsNullOrEmpty(logMagpie)) ConLogDeMagpie(logMagpie, ti);
            }
            catch (Exception ex) { fallos++; sb.AppendLine("EXCEPCION: " + ex); }
            finally
            {
                Config.DlssPickSoloPrueba(modeloAntes);
                EscalaAuto.Uso = usoAntes;
                EscalaAuto.Reiniciar(null);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            sb.AppendLine();
            sb.AppendLine(pruebas + " comprobaciones, " + fallos + " fallo(s)");
            string f = string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_escala.txt") : informe;
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(f))); File.WriteAllText(f, sb.ToString(), new UTF8Encoding(false)); } catch { }
            return fallos;
        }

        // ------------------------------------------------------------------ 1) el modelo
        static void Modelo(G[] gs, G ti, G r2060, G r3060, G r4090)
        {
            sb.AppendLine("== 1) El % de DLSS 5 que sale de la cadena (salida 4K salvo lo dicho) ==");
            string[] modelos = { "E", "K", "M", "L" };
            foreach (G g in gs)
            {
                sb.AppendLine("-- " + g.Nombre);
                foreach (string ml in modelos)
                    sb.AppendLine("  1440p 30 + DLSS FG -> 4K, DLSS " + ml + ": " + P3(Cadena(g, 30, "2560x1440", "2560x1440", "dlss", "dlss", EscalaAuto.Numero(ml), false, 3840, 2160)));
                sb.AppendLine("  1440p 30 sin FG -> 4K, E: " + P3(Cadena(g, 30, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160)));
                sb.AppendLine("  1440p 60 + DLSS FG (ligero) -> 4K, E: " + P3(Cadena(g, 60, "2560x1440", "2560x1440", "dlss", "dlss", 5, true, 3840, 2160)));
                sb.AppendLine("  1440p 60 + Smooth Motion (ligero) -> 4K, E: " + P3(Cadena(g, 60, "2560x1440", "2560x1440", "smooth", "dlss", 5, true, 3840, 2160)));
                sb.AppendLine("  1440p 60 sin FG (ligero) -> 4K, E: " + P3(Cadena(g, 60, "2560x1440", "2560x1440", "", "dlss", 5, true, 3840, 2160)));
                sb.AppendLine("  1440p 120 -> 4K, E: " + P3(Cadena(g, 120, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160)));
                sb.AppendLine("  1080p (ventana 1080p) 120 -> 4K, E: " + P3(Cadena(g, 120, "1920x1080", "1920x1080", "", "dlss", 5, false, 3840, 2160)));
                sb.AppendLine("  1080p (ventana 1080p) 30 + DLSS FG -> 4K, K: " + P3(Cadena(g, 30, "1920x1080", "1920x1080", "dlss", "dlss", 11, false, 3840, 2160)));
                sb.AppendLine("  4K 60 sin FG (ligero): " + P3(Cadena(g, 60, "3840x2160", "3840x2160", "", "dlss", 5, true, 3840, 2160)));
                sb.AppendLine("  4K 30 + DLSS FG: " + P3(Cadena(g, 30, "3840x2160", "3840x2160", "dlss", "dlss", 5, false, 3840, 2160)));
                sb.AppendLine("  1440p 30 + XeSS FG -> 4K, E: " + P3(Cadena(g, 30, "2560x1440", "2560x1440", "xess", "dlss", 5, false, 3840, 2160)));
                sb.AppendLine("  1440p 30 + DLSS FG en un monitor de 1440p (sin escalar): " + P3(Cadena(g, 30, "2560x1440", "2560x1440", "dlss", "dlss", 5, false, 2560, 1440)));
            }
            sb.AppendLine();
            sb.AppendLine("-- Lo que tiene que cumplir");
            foreach (G g in gs)
                for (int n = 1; n <= 3; n++)
                {
                    int pe = R(Pct(Cadena(g, 30, "2560x1440", "2560x1440", "dlss", "dlss", 5, false, 3840, 2160), n)),
                        pk = R(Pct(Cadena(g, 30, "2560x1440", "2560x1440", "dlss", "dlss", 11, false, 3840, 2160), n)),
                        pm = R(Pct(Cadena(g, 30, "2560x1440", "2560x1440", "dlss", "dlss", 13, false, 3840, 2160), n)),
                        pl = R(Pct(Cadena(g, 30, "2560x1440", "2560x1440", "dlss", "dlss", 12, false, 3840, 2160), n));
                    Ok(pe >= pk && pk >= pm && pm >= pl, g.Nombre + ", " + n + " pasada(s), 1440p 30 + FG: un DLSS SR mas ligero deja mas % (E " + pe + " >= K " + pk + " >= M " + pm + " >= L " + pl + ")");
                }
            foreach (G g in gs)
            {
                CadenaEscala c = Cadena(g, 30, "2560x1440", "2560x1440", "dlss", "dlss", 11, false, 3840, 2160);
                Ok(R(Pct(c, 1)) >= R(Pct(c, 2)) && R(Pct(c, 2)) >= R(Pct(c, 3)), g.Nombre + ": menos pasadas, mas % (" + P3(c) + ")");
                int p30 = R(Pct(Cadena(g, 30, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160), 1)),
                    p60 = R(Pct(Cadena(g, 60, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160), 1)),
                    p120 = R(Pct(Cadena(g, 120, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160), 1));
                Ok(p30 >= p60 && p60 >= p120, g.Nombre + ": el modo de menos fps deja mas % (30 " + p30 + " >= 60 " + p60 + " >= 120 " + p120 + ")");
            }
            string[] esc = { "30fgE", "30fgM", "60fg", "30", "120", "4k60" };
            foreach (string e in esc)
            {
                int[] v = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    G g = new[] { r2060, r3060, ti, r4090 }[i];
                    CadenaEscala c = e == "30fgE" ? Cadena(g, 30, "2560x1440", "2560x1440", "dlss", "dlss", 5, false, 3840, 2160) :
                                     e == "30fgM" ? Cadena(g, 30, "2560x1440", "2560x1440", "dlss", "dlss", 13, false, 3840, 2160) :
                                     e == "60fg" ? Cadena(g, 60, "2560x1440", "2560x1440", "dlss", "dlss", 5, true, 3840, 2160) :
                                     e == "30" ? Cadena(g, 30, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160) :
                                     e == "120" ? Cadena(g, 120, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160) :
                                     Cadena(g, 60, "3840x2160", "3840x2160", "", "dlss", 5, true, 3840, 2160);
                    v[i] = R(Pct(c, 1));
                }
                Ok(v[0] <= v[1] && v[1] <= v[2] && v[2] <= v[3], "escenario " + e + ": mejor grafica, mas % (2060 " + v[0] + " <= 3060 " + v[1] + " <= 4060 Ti " + v[2] + " <= 4090 " + v[3] + ")");
            }
            // Lo medido en Prisma (4060 Ti con OC, imagen de Ocarina en movimiento, 1440p -> 4K, DLSS SR E): +-15 puntos
            Cerca(Cadena(ti, 30, "2560x1440", "2560x1440", "dlss", "dlss", 5, false, 3840, 2160), new[] { 85, 60, 50 }, "30 + DLSS FG, E");
            Cerca(Cadena(ti, 30, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160), new[] { 100, 80, 65 }, "30 sin FG, E");
            Cerca(Cadena(ti, 60, "2560x1440", "2560x1440", "dlss", "dlss", 5, true, 3840, 2160), new[] { 40, -2, -2 }, "60 + DLSS FG ligero, E");
            Cerca(Cadena(ti, 30, "2560x1440", "2560x1440", "xess", "dlss", 5, false, 3840, 2160), new[] { 70, 45, 35 }, "30 + XeSS FG, E");
            // (M: 16-11, 70 % con la grafica al 80 %: con ese uso)
            double uso = EscalaAuto.Uso; EscalaAuto.Uso = 0.80;
            Cerca(Cadena(ti, 30, "2560x1440", "2560x1440", "dlss", "dlss", 13, false, 3840, 2160), new[] { 70, -2, -2 }, "30 + DLSS FG, M (16-11: M 70 % = 80 % de grafica; uso 80 %)");
            EscalaAuto.Uso = uso;
            int ti120 = Pct(Cadena(ti, 120, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160), 1);
            Ok(ti120 < 0 || ti120 <= 25, "4060 Ti a 120 fps (1440p -> 4K): DLSS 5 no cabe o al minimo (" + ti120 + "; log del 21-11 19:12: al 35 % Magpie recibia ~100 de 120)");
            int ue = Pct(Cadena(ti, 30, "2560x1440", "2560x1440", "dlss", "dlss", 5, false, 3840, 2160), 1), um = Pct(Cadena(ti, 30, "2560x1440", "2560x1440", "dlss", "dlss", 13, false, 3840, 2160), 1);
            Ok(ue - um >= 10, "el caso del usuario (1440p 30 + FG -> 4K, 4060 Ti): con E el % sube y con M baja (E " + ue + " frente a M " + um + "; antes 40 con cualquiera)");
            int p2060 = Pct(Cadena(r2060, 30, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160), 2);
            Ok(p2060 < 0, "RTX 2060 (6 GB): sin 2 pasadas (" + p2060 + ")");
            sb.AppendLine();
        }

        static void Cerca(CadenaEscala c, int[] medido, string que)
        {
            for (int n = 1; n <= 3; n++)
            {
                if (medido[n - 1] == -2) continue;
                int p = Pct(c, n);
                Ok(p > 0 && Math.Abs(p - medido[n - 1]) <= 15, "4060 Ti " + que + ", " + n + " pasada(s): " + p + " % (medido en Prisma " + medido[n - 1] + " %)");
            }
        }

        // ------------------------------------------------------------------ 2) las decisiones
        static EscalaAuto.Medida M(double real, double fuente, double obj, double uso)
        {
            EscalaAuto.Medida m = new EscalaAuto.Medida();
            m.Real = real; m.RealMin = real; m.Fuente = fuente; m.Objetivo = obj; m.Uso = uso; m.Segundos = 5;
            return m;
        }

        static EscalaAuto.Decision Ventanas(CadenaEscala c, EscalaAuto.Medida m, EscalaAuto.Seguimiento s, EscalaAuto.Opciones o, int veces)
        {
            EscalaAuto.Decision d = null;
            for (int i = 0; i < veces; i++) { d = EscalaAuto.Decidir(c, m, s, o); if (d.Tipo != "nada") return d; }
            return d;
        }

        static void Decisiones(G ti)
        {
            sb.AppendLine("== 2) Decisiones (medidas inventadas, 4060 Ti) ==");
            // a) 120 fps al 35 % (Rocket League, 21-11 19:12): ~100 de 120 con la grafica llena
            {
                CadenaEscala c = Cadena(ti, 120, "2560x1440", "2560x1440", "", "dlss", 5, false, 3840, 2160);
                EscalaAuto.Opciones o = new EscalaAuto.Opciones(); o.Pct = 35; o.Pasadas = 1;
                EscalaAuto.Seguimiento s = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision d1 = EscalaAuto.Decidir(c, M(100, 120, 120, 99), s, o), d = EscalaAuto.Decidir(c, M(100, 120, 120, 99), s, o);
                Ok(d1.Tipo == "nada" && d.Tipo == "bajar" && d.Pct == 25, "120 fps al 35 %, 100 de 120 (menos del 85 %): a los 10 s baja al 25 % (" + d1.Tipo + ", " + d.Tipo + " al " + d.Pct + " %: " + d.Motivo + ")");
                o.Pct = 25; o.Extra = d.Extra;
                EscalaAuto.Decision e = Ventanas(c, M(108, 120, 120, 99), s, o, 3);
                Ok(e.Tipo == "escalera", "120 fps al 25 % y aun 108 de 120: lo siguiente de la escalera (DLSS 5 fuera) (" + e.Tipo + ": " + e.Motivo + ")");
            }
            // b) 1440p 30 + FG con M al 50 %: 3 min llegando de sobra con la grafica al 60 % -> subida pendiente, como mucho +15
            {
                CadenaEscala c = Cadena(ti, 30, "2560x1440", "2560x1440", "dlss", "dlss", 13, false, 3840, 2160);
                EscalaAuto.Opciones o = new EscalaAuto.Opciones(); o.Pct = 50; o.Pasadas = 1;
                EscalaAuto.Seguimiento s = new EscalaAuto.Seguimiento();
                EscalaAuto.Medida m = M(29.97, 29.97, 29.97, 60);
                EscalaAuto.Decision d = Ventanas(c, m, s, o, 40);
                Ok(d.Tipo == "subir" && d.Pct > 50 && d.Pct <= 65, "30 + FG con M al 50 %, sobra (grafica 60 %) 3 min: sube para la proxima apertura (" + d.Tipo + " al " + d.Pct + " %: " + d.Motivo + ")");
                EscalaAuto.Seguimiento s2 = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision d2 = Ventanas(c, m, s2, o, 30);
                Ok(d2.Tipo == "nada", "con 2,5 min de sobra aun no sube (" + d2.Tipo + ": " + d2.Motivo + ")");
            }
            // c) 60 + FG al 40 %: 57,5 de 59,94 con la grafica al 95 % -> baja en 15 s
            {
                CadenaEscala c = Cadena(ti, 60, "2560x1440", "2560x1440", "dlss", "dlss", 5, true, 3840, 2160);
                EscalaAuto.Opciones o = new EscalaAuto.Opciones(); o.Pct = 40; o.Pasadas = 1;
                EscalaAuto.Seguimiento s = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision d = Ventanas(c, M(57.5, 59.94, 59.94, 95), s, o, 3);
                Ok(d.Tipo == "bajar" && d.Pct < 40 && d.Pct >= 25, "60 + FG al 40 %, 57,5 de 59,94: baja (" + d.Tipo + " al " + d.Pct + " %: " + d.Motivo + ")");
                EscalaAuto.Seguimiento s2 = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision d2 = Ventanas(c, M(59.4, 59.94, 59.94, 85), s2, o, 10);
                Ok(d2.Tipo == "nada", "59,4 de 59,94 (menos de 1 imagen perdida por segundo): nada (" + d2.Tipo + ": " + d2.Motivo + ")");
            }
            // d) el juego va a 30 en el modo 60 (mpv da 30): no es cosa del PC
            {
                CadenaEscala c = Cadena(ti, 60, "2560x1440", "2560x1440", "", "dlss", 5, true, 3840, 2160);
                EscalaAuto.Opciones o = new EscalaAuto.Opciones(); o.Pct = 50; o.Pasadas = 1;
                EscalaAuto.Seguimiento s = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision d = Ventanas(c, M(30, 30, 59.94, 40), s, o, 10);
                Ok(d.Tipo == "nada", "el juego a 30 en el modo 60 (mpv da 30): nada (" + d.Tipo + ": " + d.Motivo + ")");
            }
            // e) la salida de la generacion de Magpie: 96 de 119,9 con las reales enteras y la grafica llena
            {
                CadenaEscala c = Cadena(ti, 60, "2560x1440", "2560x1440", "dlss", "dlss", 5, true, 3840, 2160);
                EscalaAuto.Opciones o = new EscalaAuto.Opciones(); o.Pct = 40; o.Pasadas = 1;
                EscalaAuto.Seguimiento s = new EscalaAuto.Seguimiento();
                EscalaAuto.Medida m = M(59.94, 59.94, 59.94, 97); m.Salida = 96; m.SalidaObjetivo = 119.8;
                EscalaAuto.Decision d = Ventanas(c, m, s, o, 3);
                Ok(d.Tipo == "bajar" && d.PorSalida && d.Pct < 40, "60 + FG: la salida a 96 de 119,8 con la grafica llena: baja (" + d.Tipo + " al " + d.Pct + " %, por la salida " + d.PorSalida + ")");
                EscalaAuto.Seguimiento s2 = new EscalaAuto.Seguimiento();
                m.Uso = 70;
                EscalaAuto.Decision d2 = Ventanas(c, m, s2, o, 6);
                Ok(d2.Tipo == "nada", "la salida baja pero la grafica va al 70 % (no es la escala): nada (" + d2.Tipo + ")");
            }
            // f) mucho que bajar (3 pasadas al 40 % con M, 22 de 29,97): varios pasos juntos, una sola reapertura
            {
                CadenaEscala c = Cadena(ti, 30, "2560x1440", "2560x1440", "dlss", "dlss", 13, false, 3840, 2160);
                EscalaAuto.Opciones o = new EscalaAuto.Opciones(); o.Pct = 40; o.Pasadas = 3;
                EscalaAuto.Seguimiento s = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision d = Ventanas(c, M(22, 29.97, 29.97, 99), s, o, 2);
                Ok(d.Tipo == "bajar" && d.Pasos.Count >= 2, "3 pasadas al 40 % con M, 22 de 29,97: en 10 s, " + d.Pasos.Count + " pasos de una vez (" + string.Join("+", d.Pasos.ToArray()) +
                   ": " + d.Pasadas + " pasada(s) al " + d.Pct + " %, DLSS " + EscalaAuto.Letra(d.Modelo) + "; " + d.Motivo + ")");
            }
            // h) sin el % de grafica (sin NVAPI): sube +5 a los 10 min; baja igual (se supone la grafica llena); a los 60 s llegando, se fija
            {
                CadenaEscala c = Cadena(ti, 30, "2560x1440", "2560x1440", "dlss", "dlss", 13, false, 3840, 2160);
                EscalaAuto.Opciones o = new EscalaAuto.Opciones(); o.Pct = 50; o.Pasadas = 1;
                EscalaAuto.Seguimiento s = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision d9 = Ventanas(c, M(29.97, 29.97, 29.97, -1), s, o, 119);
                EscalaAuto.Decision d10 = EscalaAuto.Decidir(c, M(29.97, 29.97, 29.97, -1), s, o);
                Ok(d9.Tipo == "nada" && d10.Tipo == "subir" && d10.Pct == 55, "sin % de grafica: a los 10 min de sobra +5 (" + d9.Tipo + ", luego " + d10.Tipo + " al " + d10.Pct + " %)");
                EscalaAuto.Seguimiento s2 = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision dn = Ventanas(c, M(27.0, 29.97, 29.97, -1), s2, o, 3);
                Ok(dn.Tipo == "bajar" && dn.Pct < 50, "sin % de grafica, 27 de 29,97: baja (" + dn.Tipo + " al " + dn.Pct + " %)");
                EscalaAuto.Seguimiento s3 = new EscalaAuto.Seguimiento();
                bool fijo = false; int cuando = 0;
                for (int i = 1; i <= 20 && !fijo; i++) { EscalaAuto.Decision df = EscalaAuto.Decidir(c, M(29.9, 29.97, 29.97, 88), s3, o); if (df.Fijar) { fijo = true; cuando = i * 5; } }
                Ok(fijo && cuando == 60, "llegando (aunque justo, grafica 88 %): a los " + cuando + " s se fija lo de ahora (no lo cambia un k nuevo)");
            }
            // g) sin vaivenes: sube (pendiente), se abre Magpie, no llega -> baja y ya no vuelve al que fallo
            {
                CadenaEscala c = Cadena(ti, 30, "2560x1440", "2560x1440", "dlss", "dlss", 11, false, 3840, 2160);
                c.Juego = "Juego de prueba";
                int p0 = EscalaAuto.PctPara(c, 1);
                EscalaAuto.Opciones o = new EscalaAuto.Opciones(); o.Pct = p0; o.Pasadas = 1; o.K = 1;
                // (para la prueba: que el modelo se haya quedado corto: la estimacion tiene 4 ms de extra aprendido)
                EscalaAuto.AnotarExtra(c.Juego, c.Clave, 4, 1.0);
                EscalaAuto.AlAbrirMagpie();
                int p1 = EscalaAuto.PctPara(c, 1);
                o.Pct = p1; o.Extra = EscalaAuto.Extra(c.Juego, c.Clave);
                EscalaAuto.Seguimiento s = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision up = Ventanas(c, M(29.97, 29.97, 29.97, 55), s, o, 40);
                bool subio = up.Tipo == "subir";
                if (subio) EscalaAuto.AnotarSubida(c.Juego, c.Clave, up.Extra, up.K);
                int sigueIgual = EscalaAuto.PctPara(c, 1);   // (congelado: Magpie sigue abierto)
                EscalaAuto.AlAbrirMagpie();
                int p2 = EscalaAuto.PctPara(c, 1);
                Ok(subio && sigueIgual == p1 && p2 > p1, "sube: " + p1 + " % -> " + up.Pct + " % pendiente; con Magpie abierto sigue " + sigueIgual + " %; al abrirlo " + p2 + " %");
                // no llega al nuevo
                o.Pct = p2; o.Extra = EscalaAuto.Extra(c.Juego, c.Clave);
                EscalaAuto.Seguimiento s2 = new EscalaAuto.Seguimiento();
                EscalaAuto.Decision dn = Ventanas(c, M(27.5, 29.97, 29.97, 99), s2, o, 3);
                if (dn.Tipo == "bajar") EscalaAuto.AnotarBajada(c.Juego, c.Clave, c.ClaveCompleta(1), p2, dn.Extra, dn.K, 0, "", c.ClaveCompleta(dn.Pasadas), dn.Pct);
                int p3 = EscalaAuto.PctPara(c, 1);
                Ok(dn.Tipo == "bajar" && p3 < p2, "no llega al " + p2 + " %: baja ya al " + p3 + " % (" + dn.Motivo + ")");
                // otra vez de sobra mucho rato: como mucho hasta el que fallo - 5
                EscalaAuto.AlAbrirMagpie();
                int p4 = EscalaAuto.PctPara(c, 1);
                o.Pct = p4; o.Extra = EscalaAuto.Extra(c.Juego, c.Clave); o.NoPct = EscalaAuto.NoPct(c.Juego, c.ClaveCompleta(1));
                EscalaAuto.Seguimiento s3 = new EscalaAuto.Seguimiento();
                int maxVisto = p4;
                for (int ciclo = 0; ciclo < 6; ciclo++)
                {
                    EscalaAuto.Decision u = Ventanas(c, M(29.97, 29.97, 29.97, 40), s3, o, 40);
                    if (u.Tipo == "subir") EscalaAuto.AnotarSubida(c.Juego, c.Clave, u.Extra, u.K);
                    EscalaAuto.AlAbrirMagpie();
                    int pv = EscalaAuto.PctPara(c, 1);
                    maxVisto = Math.Max(maxVisto, pv);
                    o.Pct = pv; o.Extra = EscalaAuto.Extra(c.Juego, c.Clave);
                }
                Ok(o.NoPct == p2 && maxVisto <= p2 - 5, "tras no llegar al " + p2 + " % (no_pct " + o.NoPct + "), 6 ciclos de sobra: como mucho " + maxVisto + " % (nunca el que fallo)");
                // otro juego empieza por lo aprendido en cualquier juego ("*")
                CadenaEscala otro = c.Copia(); otro.Juego = "Otro juego";
                EscalaAuto.AlAbrirMagpie();
                int po = EscalaAuto.PctPara(otro, 1);
                CadenaEscala nada = c.Copia(); nada.Juego = "Otro juego"; nada.CapW = 1280; nada.CapH = 720;   // (otra clave: nada aprendido)
                int pn = EscalaAuto.PctPara(nada, 1);
                Ok(po <= pn, "un juego nuevo empieza por lo aprendido en cualquiera (" + po + " % frente a " + pn + " % sin nada aprendido)");
            }
            sb.AppendLine();
        }

        // ------------------------------------------------------------------ 4) con el ini de este PC (solo se lee) y su grafica (data\hardware.txt)
        static void ConTuIni()
        {
            sb.AppendLine("== 4) Con el ini de este PC (" + Paths.IniFile + ", solo lectura) y su grafica ==");
            if (!File.Exists(Paths.IniFile)) { sb.AppendLine("  (no hay ini)"); return; }
            Forzar("dlss5State", 1); Forzar("dlssgState", 1);
            bool w1080 = Config.Window1080;
            try
            {
                EscalaAuto.Reiniciar(Path.Combine(Path.GetTempPath(), "ce_test_escala_ini.txt"));
                Config cfg = Config.Load();
                cfg.EngineDlss5 = false;   // (1.2.3 MOTOR5: esto prueba la escala de Magpie; con motor_dlss5=1 y PrismaNr.dll DLSS 5 lo haria el motor)
                CadenaEscala g0 = new CadenaEscala(); EscalaAuto.GpuPara(g0);
                int ow, oh; EscalaAuto.Salida(out ow, out oh);
                sb.AppendLine("  grafica: indice " + g0.Indice.ToString("0.##", CultureInfo.InvariantCulture) + ", " + g0.Familia + ", " + g0.VramGb + " GB; pantalla " + ow + "x" + oh +
                              "; ventana_1080 " + (Config.Window1080 ? "si" : "no") + "; escala automatica " + (cfg.Dlss5AutoAhora ? "si" : "no"));
                int menosE = -1, menosM = -1;
                foreach (string cap in new[] { "2560x1440", "1920x1080", "3840x2160" })
                    foreach (int modo in new[] { 30, 60, 120 })
                        foreach (int modelo in new[] { 5, 13 })
                        {
                            if (modo == 120 && (cap == "3840x2160" || !cfg.Captura120Ok(cap))) continue;
                            EscalaAuto.Reiniciar(Path.Combine(Path.GetTempPath(), "ce_test_escala_ini.txt"));
                            cfg.VideoSize = cap; cfg.CapMode = modo == 30; cfg.Mode120 = modo == 120; cfg.FgOff = false; cfg.Dlss5Off = false; cfg.Passes30 = 1; cfg.DlssModel = modelo;
                            bool auto = cfg.Dlss5AutoOn;
                            cfg.Dlss5AutoOn = false;
                            int antes = cfg.Dlss5FitsNow ? cfg.Dlss5PctNow : -1;
                            cfg.Dlss5AutoOn = auto;
                            List<object> fx = MagpieConfig.Chain(cfg, double.NaN);
                            Dictionary<string, object> nr = Efecto(fx, "DLSSNR\\DLSSNR_AI_Filter");
                            int ahora = cfg.Dlss5FitsNow ? cfg.Dlss5PctNow : -1;
                            if (cap == "2560x1440" && modo == 30) { if (modelo == 5) menosE = ahora; else menosM = ahora; }
                            sb.AppendLine("  " + cfg.ProfileText + " | antes (tablas) " + antes + " %, ahora " + ahora + " %, Magpie inputResolutionPercent " +
                                          (nr != null ? Num(nr, "inputResolutionPercent").ToString(CultureInfo.InvariantCulture) : "(sin DLSSNR)"));
                        }
                Ok(menosE > menosM, "con tu ini, 1440p 30 + FG -> pantalla: con DLSS E mas % que con M (" + menosE + " frente a " + menosM + ")");
            }
            catch (Exception ex) { fallos++; sb.AppendLine("  EXCEPCION: " + ex.Message); }
            finally { Config.Window1080 = w1080; Forzar("dlss5State", 0); Forzar("dlssgState", 0); try { File.Delete(Path.Combine(Path.GetTempPath(), "ce_test_escala_ini.txt")); } catch { } }
            sb.AppendLine();
        }

        // ------------------------------------------------------------------ 5) una sesion de verdad: el log de Magpie (solo se lee)
        // Sus resumenes "Output cadence" (como ReadLsStats: nuevas / tiempo) en ventanas de 5 s, cada apertura de Magpie con DLSS 5 por
        // separado (su % de "DLSSNR STATUS" y su ritmo de "Frame sync effective base target"), desde los 25 s, por Decidir: lo que habria hecho
        static void ConLogDeMagpie(string ruta, G ti)
        {
            sb.AppendLine("== 5) El log de Magpie de una sesion de verdad (" + ruta + ", solo lectura) ==");
            if (!File.Exists(ruta)) { sb.AppendLine("  (no esta)"); return; }
            string[] ls;
            using (FileStream fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader rd = new StreamReader(fs, Encoding.UTF8)) ls = rd.ReadToEnd().Split('\n');
            System.Text.RegularExpressions.Regex rOc = new System.Text.RegularExpressions.Regex(@"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\|.*Output cadence .*?captureFps=([0-9.]+).*?newCapture=(\d+).*?generated=(\d+)");
            System.Text.RegularExpressions.Regex rSt = new System.Text.RegularExpressions.Regex(@"DLSSNR STATUS: .*?sourceSize=(\d+)x(\d+).*?inputResolutionPercent=(\d+)");
            System.Text.RegularExpressions.Regex rTg = new System.Text.RegularExpressions.Regex(@"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\|.*Frame sync effective base target: ([0-9.]+) FPS");
            int sesiones = 0;
            double objetivo = 0; int pct = 0, venH = 0, venW = 0; DateTime inicio = DateTime.MinValue, ventana = DateTime.MinValue;
            double nuevas = 0, tiempo = 0, gen = 0; int resumenes = 0;
            EscalaAuto.Seguimiento s = new EscalaAuto.Seguimiento();
            bool decidido = false;
            List<string> linea = new List<string>();
            foreach (string raw in ls)
            {
                string l = raw.TrimEnd('\r');
                System.Text.RegularExpressions.Match m;
                if ((m = rTg.Match(l)).Success)
                {
                    // otra apertura de Magpie
                    objetivo = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    inicio = DateTime.ParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    pct = 0; ventana = DateTime.MinValue; nuevas = tiempo = gen = 0; resumenes = 0; s = new EscalaAuto.Seguimiento(); decidido = false;
                    continue;
                }
                if ((m = rSt.Match(l)).Success) { venW = int.Parse(m.Groups[1].Value); venH = int.Parse(m.Groups[2].Value); pct = int.Parse(m.Groups[3].Value); sesiones++; continue; }
                if (pct <= 0 || decidido || !(m = rOc.Match(l)).Success) continue;
                DateTime ts = DateTime.ParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                if ((ts - inicio).TotalSeconds < 25) continue;
                double cf = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int nc = int.Parse(m.Groups[3].Value), gn = int.Parse(m.Groups[4].Value);
                if (cf <= 0.5 || nc <= 0) continue;
                if (ventana == DateTime.MinValue) ventana = ts;
                nuevas += nc; gen += gn; tiempo += nc / cf; resumenes++;
                if ((ts - ventana).TotalSeconds < 5) continue;
                // una ventana de 5 s, como CheckLsPacing
                int modo = objetivo > 100 ? 120 : objetivo > 40 ? 60 : 30;
                string capt = venH >= 2000 ? "3840x2160" : venH >= 1400 ? "2560x1440" : "1920x1080";
                CadenaEscala c = Cadena(ti, modo, capt, capt, gen > 0 ? "dlss" : "", "dlss", 5, modo != 30, 3840, 2160);
                EscalaAuto.Opciones o = new EscalaAuto.Opciones(); o.Pct = pct; o.Pasadas = 1;
                EscalaAuto.Medida md = M(nuevas / tiempo, modo == 120 ? 120 : objetivo, modo == 120 ? 120 : objetivo, -1);
                EscalaAuto.Decision d = EscalaAuto.Decidir(c, md, s, o);
                linea.Add(F(md.Real));
                if (d.Tipo != "nada")
                {
                    sb.AppendLine("  " + inicio.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " modo " + modo + ", " + capt + ", DLSS 5 al " + pct + " %: reales por ventana " +
                                  string.Join(" ", linea.ToArray()) + " -> " + d.Tipo + (d.Tipo == "bajar" ? " al " + d.Pct + " %" : "") + " (" + d.Motivo + ")");
                    decidido = true;
                    if (modo == 120) Ok(d.Tipo == "bajar" || d.Tipo == "escalera", "la sesion de 120 fps al " + pct + " % del log: se habria bajado (" + d.Tipo + ")");
                }
                ventana = ts; nuevas = tiempo = gen = 0; resumenes = 0;
                if (linea.Count > 40) { sb.AppendLine("  " + inicio.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " modo " + modo + " al " + pct + " %: 40 ventanas sin decidir nada (" + d.Motivo + ")"); decidido = true; }
                if (decidido) linea.Clear();
            }
            sb.AppendLine("  " + sesiones + " apertura(s) de Magpie con DLSS 5 en el log");
            sb.AppendLine();
        }

        static string F(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        // ------------------------------------------------------------------ 3) la config de Magpie
        static void Forzar(string campo, int v)
        {
            FieldInfo f = typeof(Integrado).GetField(campo, BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) f.SetValue(null, v);
        }

        static Dictionary<string, object> Efecto(List<object> fx, string nombre)
        {
            foreach (object o in fx)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d != null && (d["name"] as string) == nombre) return d.ContainsKey("parameters") ? d["parameters"] as Dictionary<string, object> : new Dictionary<string, object>();
            }
            return null;
        }

        static double Num(Dictionary<string, object> p, string k) { return p != null && p.ContainsKey(k) ? Convert.ToDouble(p[k], CultureInfo.InvariantCulture) : double.NaN; }

        static void ConfigMagpie(G ti, G r4090)
        {
            sb.AppendLine("== 3) La config de Magpie que se escribiria (MagpieConfig.Chain, Config en memoria) ==");
            Forzar("dlss5State", 1);   // (como si estuviera nvngx_dlssnr.dll en magpie\)
            Forzar("dlssgState", 1);   // (y runtimes\dlssg_sm86: DLSS FG por Magpie)
            EscalaAuto.SalidaPrueba(3840, 2160);
            bool w1080 = Config.Window1080;
            try
            {
                foreach (G g in new[] { ti, r4090 })
                {
                    EscalaAuto.GpuFija(g.Indice, g.Fam, g.Vram);
                    foreach (int modelo in new[] { 5, 13 })
                        foreach (int modo in new[] { 30, 60, 120 })
                            for (int n = 1; n <= 2; n++)
                            {
                                EscalaAuto.Reiniciar(Path.Combine(Path.GetTempPath(), "ce_test_escala_cfg.txt"));
                                Config cfg = new Config();
                                cfg.VideoSize = "2560x1440"; cfg.CapMode = modo == 30; cfg.Mode120 = modo == 120; cfg.FgOff = false; cfg.Dlss5Off = false;
                                cfg.Capturas120 = new[] { "1920x1080", "2560x1440" }; cfg.Framerate120 = "120";
                                cfg.Passes30 = n; cfg.DlssModel = modelo;
                                Config.Window1080 = false;
                                List<object> fx = MagpieConfig.Chain(cfg, double.NaN);
                                Dictionary<string, object> nr = Efecto(fx, "DLSSNR\\DLSSNR_AI_Filter"), sr = Efecto(fx, "DLSS\\DLSS_SR");
                                int pct = cfg.Dlss5PctNow, pas = cfg.Dlss5PassesNow;
                                CadenaEscala c = cfg.CadenaEscalaPara(Math.Max(1, pas));
                                int esperado = pas > 0 ? EscalaAuto.Mejor(c, pas, 1.0, 0) : -1;
                                string que = g.Nombre + ", 1440p modo " + modo + (modo < 120 ? " + DLSS FG" : "") + ", DLSS " + EscalaAuto.Letra(modelo) + ", " + n + " pasada(s) pedida(s)";
                                if (pas <= 0 || !cfg.Dlss5FitsNow)
                                {
                                    Ok(nr == null, que + ": DLSS 5 no cabe y Magpie va sin DLSSNR (" + (nr == null ? "sin DLSSNR" : "CON DLSSNR") + ")");
                                    continue;
                                }
                                double ip = Num(nr, "inputResolutionPercent"), rc = Num(nr, "resolutionControl"), mp = Num(nr, "multiPass"), rp = Num(sr, "renderPreset");
                                Ok(nr != null && ip == pct && pct == esperado && rc == 1 && mp == pas && (sr == null || rp == modelo),
                                   que + ": inputResolutionPercent " + ip + " (Dlss5PctNow " + pct + ", modelo " + esperado + "), resolutionControl " + rc + ", multiPass " + mp +
                                   " (" + pas + "), renderPreset " + rp + " | " + cfg.ProfileText);
                            }
                }
            }
            finally
            {
                Config.Window1080 = w1080;
                Forzar("dlss5State", 0); Forzar("dlssgState", 0);
                EscalaAuto.SalidaFija = false;
                EscalaAuto.GpuFija(-1, "rtx40", 8);
                try { File.Delete(Path.Combine(Path.GetTempPath(), "ce_test_escala_cfg.txt")); } catch { }
            }
            sb.AppendLine();
        }
    }
}
