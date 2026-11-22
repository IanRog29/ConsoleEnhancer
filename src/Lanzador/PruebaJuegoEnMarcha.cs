// PruebaJuegoEnMarcha.cs - 22-11 (1.2.3, agente JUEGO): prueba sin consola de src\JuegoEnMarcha.cs (y del experimento de la huella del HUD):
//   ConsoleEnhancer.exe --datos=<COPIA de los datos> --test-juego-en-marcha=<informe>[;<carpeta con capturas de juegos>]
// 1. FocoVisita (el primer juego en foco de una visita al menu) con iconos de verdad (capturas del inicio de la PS5 y console_icons.txt)
// 2. La visita corta del 22-11 10:26:31 con el ritmo de capturas de la 1.2.2 (sacado del log) y el de ahora
// 3. El perfil nuevo de la consola con el modo guardado (games\mode.txt de la copia)
// 4. La huella del HUD (reconocer el juego sin el menu): aciertos, fallos y "no se sabe" con capturas de verdad. La carpeta de capturas:
//    ficheros <juego>__<sesion>__<nombre>.png (juego = gta / bb / rl / ... / otro); se aprende con una sesion y se prueba con las demas
// Salida = fallos. C# 5
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
    static class PruebaJuegoEnMarcha
    {
        static int fallos;
        static StringBuilder sb;

        static void Mira(bool ok, string que)
        {
            sb.Append(ok ? "  OK    " : "  FALLO ").Append(que).Append("\r\n");
            if (!ok) fallos++;
        }

        static string F1(double d) { return d.ToString("0.0", CultureInfo.InvariantCulture); }
        static string F2(double d) { return d.ToString("0.00", CultureInfo.InvariantCulture); }

        public static int Run(string arg)
        {
            string[] p = arg.Split(';');
            string rep = p[0];
            string capturas = p.Length > 1 && p[1].Length > 0 ? p[1] : null;
            sb = new StringBuilder();
            fallos = 0;
            sb.Append("1.2.3 (JUEGO): el juego en marcha (src\\JuegoEnMarcha.cs). Datos: ").Append(Paths.Datos).Append("\r\n");
            try { Visita(); } catch (Exception ex) { Mira(false, "visita: " + ex); }
            try { Ritmo(); } catch (Exception ex) { Mira(false, "ritmo: " + ex); }
            try
            {
                sb.Append("\r\n-- 2b. Cuando se captura para leer el primer foco (estado de la app puesto a mano) --\r\n");
                int fc;
                sb.Append(TrayApp.CapturasSelfTest(out fc));
                fallos += fc;
            }
            catch (Exception ex) { Mira(false, "capturas: " + ex); }
            try { Modo(); } catch (Exception ex) { Mira(false, "modo: " + ex); }
            try { if (capturas != null) Hud(capturas); } catch (Exception ex) { Mira(false, "huella del HUD: " + ex); }
            try { Degradadas(); } catch (Exception ex) { Mira(false, "etiqueta degradada: " + ex); }
            sb.Insert(0, "Fallos: " + fallos + "\r\n");
            File.WriteAllText(rep, sb.ToString(), new UTF8Encoding(false));
            return fallos;
        }

        static Bitmap Carga(string f)
        {
            using (Bitmap src = new Bitmap(f))
            {
                Bitmap full = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(full)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                return full;
            }
        }

        // ---------------- 1. FocoVisita ----------------
        static List<double[]> IconosGuardados(string perfil)
        {
            List<double[]> r = new List<double[]>();
            string f = Path.Combine(Paths.Datos, @"games\console_icons.txt");
            if (!File.Exists(f)) return r;
            foreach (string raw in File.ReadAllLines(f, Encoding.UTF8))
            {
                int bar = raw.LastIndexOf('|');
                if (raw.StartsWith("#") || bar <= 0 || raw.Substring(0, bar) != perfil) continue;
                string[] nums = raw.Substring(bar + 1).Split(',');
                if (nums.Length != JuegoConsola.IcoN * JuegoConsola.IcoN) continue;
                double[] v = new double[nums.Length];
                for (int k = 0; k < v.Length; k++) v[k] = double.Parse(nums[k], CultureInfo.InvariantCulture);
                r.Add(v);
            }
            return r;
        }

        static void Visita()
        {
            sb.Append("\r\n-- 1. El primer juego en foco de la visita (FocoVisita), con iconos de verdad --\r\n");
            // Las capturas del inicio de la PS5 con GTA VI en foco (banco: menu_ce-gta-pestanas-*)
            List<double[]> gta = new List<double[]>();
            string banco = @"C:\Switch2Magpie\juegos\test_deteccion";
            if (Directory.Exists(banco))
                foreach (string f in Directory.GetFiles(banco, "menu_ce-gta-pestanas-*.png"))
                    using (Bitmap b = Carga(f))
                    {
                        double[] ic = JuegoConsola.IconoFoco(b);
                        if (ic != null && !JuegoConsola.PareceFundido(ic)) gta.Add(ic);
                    }
            List<double[]> rl = IconosGuardados("PS5: Rocket"), pr = IconosGuardados("PS5: PLAYÑOOM"), gtaG = IconosGuardados("PS5: Grand Theft Auto VI");
            sb.Append("  (iconos: ").Append(gta.Count).Append(" de GTA VI de capturas del inicio; guardados del usuario: GTA VI ").Append(gtaG.Count)
              .Append(", Rocket ").Append(rl.Count).Append(", PLAYROOM ").Append(pr.Count).Append(")\r\n");
            if (gta.Count < 2 || rl.Count == 0) { Mira(false, "faltan iconos de verdad para la prueba"); return; }
            // Distancias de verdad (MismoIcono = 22): el mismo juego en otra captura y otro juego
            double dMismo = 0, dOtro = 999, dOtroPr = 999;
            for (int i = 1; i < gta.Count; i++) dMismo = Math.Max(dMismo, JuegoConsola.DistHuella(gta[0], gta[i]));
            foreach (double[] g in gta) { foreach (double[] r in rl) dOtro = Math.Min(dOtro, JuegoConsola.DistHuella(g, r)); foreach (double[] r in pr) dOtroPr = Math.Min(dOtroPr, JuegoConsola.DistHuella(g, r)); }
            double dGuardado = 999;
            foreach (double[] g in gtaG) dGuardado = Math.Min(dGuardado, JuegoConsola.DistHuella(gta[0], g));
            sb.Append("  distancias: GTA VI con GTA VI (otras capturas) hasta ").Append(F1(dMismo)).Append(", con el guardado del usuario ").Append(F1(dGuardado))
              .Append("; GTA VI con Rocket League ").Append(F1(dOtro)).Append(", con PLAYROOM ").Append(F1(dOtroPr)).Append(" (mismo icono < ").Append(F1(JuegoConsola.MismoIcono)).Append(")\r\n");
            Mira(dMismo < JuegoConsola.MismoIcono && dOtro >= JuegoConsola.MismoIcono, "el icono en foco separa el mismo juego de otro");
            double dpr;
            string perfilIc = IconosConsola.Busca(gta[1], out dpr);
            Mira(perfilIc == "PS5: Grand Theft Auto VI", "el icono de una captura del inicio da el perfil del usuario: \"" + perfilIc + "\" (" + F1(dpr) + ")");

            DateTime t0 = new DateTime(2026, 11, 22, 10, 26, 31, 893);
            FocoVisita v = new FocoVisita();
            // a) leido enseguida (1.2.05)
            v.Empieza(t0);
            Mira(v.PorLeer(t0.AddSeconds(0.2)), "a) recien visto el menu: se busca el foco (capturas cada 0,25 s)");
            bool ok = v.Observa(t0.AddSeconds(0.4), "Grand Theft Auto VI", gta[0]);
            Mira(ok && v.Nombre == "Grand Theft Auto VI" && !v.PorLeer(t0.AddSeconds(0.5)), "a) leido a los 0,4 s: \"" + v.Nombre + "\" (" + v.Porque + ")");
            // b) el caso de 10:26:31: el inicio apareciendo (sin etiqueta) y la etiqueta ya pasando al menu, con el mismo icono
            v.Reinicia(); v.Empieza(t0);
            v.Observa(t0, null, gta[2]); v.Observa(t0.AddSeconds(0.38), null, gta[3 % gta.Count]);
            ok = v.Observa(t0.AddSeconds(1.9), "Grand Theft Auto VI", gta[1]);
            Mira(ok && v.Nombre == "Grand Theft Auto VI", "b) sin etiqueta al principio, leida a los 1,9 s con el mismo icono: \"" + v.Nombre + "\" (" + v.Porque + ")");
            // c) el foco se movio a otro juego antes de la primera lectura: no vale
            v.Reinicia(); v.Empieza(t0);
            v.Observa(t0, null, gta[2]); v.Observa(t0.AddSeconds(0.38), null, gta[0]);
            ok = v.Observa(t0.AddSeconds(1.9), "Rocket League", rl[0]);
            Mira(!ok && v.Nombre == null && v.Cerrada && !v.PorLeer(t0.AddSeconds(2)), "c) leido a los 1,9 s con otro icono (el foco se movio): no se toma (" + v.Porque + ")");
            // d) sin iconos del principio: no vale pasado 1,5 s
            v.Reinicia(); v.Empieza(t0);
            ok = v.Observa(t0.AddSeconds(2.2), "Grand Theft Auto VI", gta[0]);
            Mira(!ok && v.Nombre == null, "d) leido a los 2,2 s sin iconos del principio: no se toma (" + v.Porque + ")");
            // e) pasado el plazo
            v.Reinicia(); v.Empieza(t0);
            v.Observa(t0.AddSeconds(0.3), null, gta[0]);
            ok = v.Observa(t0.AddSeconds(6), "Grand Theft Auto VI", gta[0]);
            Mira(!ok && v.Nombre == null && !v.PorLeer(t0.AddSeconds(4.9)), "e) a los 6 s ya no se busca (" + v.Porque + ")");
            // f) los iconos de las capturas de 1,5 s en adelante no cuentan como "del principio"
            v.Reinicia(); v.Empieza(t0);
            v.Observa(t0.AddSeconds(1.7), null, rl[0]);
            ok = v.Observa(t0.AddSeconds(2.0), "Rocket League", rl[0]);
            Mira(!ok && v.Nombre == null, "f) un icono visto a los 1,7 s (el foco ya movido) no vale de principio");
            // h) el boton PS abrio antes el centro de control (sin el inicio ni la etiqueta) y a los 3 s se pasa al inicio: cuenta desde ahi
            v.Reinicia(); v.Empieza(t0);
            v.VeInicio(t0.AddSeconds(3));
            bool sigue = v.PorLeer(t0.AddSeconds(7));
            ok = v.Observa(t0.AddSeconds(3.4), "Grand Theft Auto VI", gta[0]);
            Mira(sigue && ok && v.Nombre == "Grand Theft Auto VI", "h) centro de control 3 s y luego el inicio: leido a los 0,4 s del inicio (" + v.Porque + ")");
            // i) VeInicio solo la primera vez (volver al inicio desde la Store no reinicia el plazo) y nunca con el foco ya sabido
            v.Reinicia(); v.Empieza(t0);
            v.VeInicio(t0.AddSeconds(0.2)); v.VeInicio(t0.AddSeconds(4));
            Mira(!v.PorLeer(t0.AddSeconds(5.3)), "i) el inicio visto otra vez no reinicia el plazo");
            // g) sin visita (la app se abrio ya en el menu): nada
            v.Reinicia();
            Mira(!v.PorLeer(t0) && !v.Observa(t0, "Grand Theft Auto VI", gta[0]), "g) sin visita desde un juego: no se busca nada");
        }

        // ---------------- 2. La visita corta del log ----------------
        // Capturas con el menu de la visita de 10:26:31 (segundos desde la primera): 1.2.2 = 0 y 0,38 (las dos que vieron el menu), nada
        // hasta 1,8 (paso al menu: lsReady=false y el margen de switchUntil, 1,4 s) y la siguiente ya volviendo al juego (fuera a los 2,52).
        // Ahora: 0, 0,38 y luego cada ~0,4 s (0,25 + el PNG a 4K, 0,10-0,19 s) tambien pasando al menu. La etiqueta: no estaba en 0 ni en
        // 0,38 (el inicio apareciendo: la copia de la pantalla del telon a los 0,58 s no se parecia a la de mpv, diferencia 26) ni en 1,8
        // (el log no leyo nada): legible solo en medio (supuesto: 0,6-1,7 s). Con el icono del principio, del inicio apareciendo
        static void Ritmo()
        {
            sb.Append("\r\n-- 2. La visita corta de 10:26:31 (2,5 s): capturas con la 1.2.2 y ahora (modelo sacado del log) --\r\n");
            double[] antes = { 0, 0.38, 1.8 };
            List<double> ahora = new List<double> { 0, 0.38 };
            for (double t = 0.78; t < 2.52; t += 0.4) ahora.Add(t);
            Func<double, bool> legible = delegate (double t) { return t >= 0.6 && t <= 1.7; };
            int la = 0, lh = 0;
            foreach (double t in antes) if (legible(t)) la++;
            foreach (double t in ahora) if (legible(t)) lh++;
            // Con FocoVisita: el primer foco a la primera lectura legible
            double[] ic = new double[JuegoConsola.IcoN * JuegoConsola.IcoN];
            for (int i = 0; i < ic.Length; i++) ic[i] = ((i * 37) % 11 - 5) * 10;
            DateTime t0 = new DateTime(2026, 11, 22, 10, 26, 31, 893);
            FocoVisita v = new FocoVisita();
            v.Empieza(t0);
            double cuando = -1;
            foreach (double t in ahora) if (v.Observa(t0.AddSeconds(t), legible(t) ? "Grand Theft Auto VI" : null, ic)) { cuando = t; break; }
            sb.Append("  1.2.2: ").Append(antes.Length).Append(" capturas con el menu, ").Append(la).Append(" con la etiqueta legible -> \"Consola\" al salir\r\n");
            sb.Append("  ahora: ").Append(ahora.Count).Append(" capturas, ").Append(lh).Append(" con la etiqueta legible -> primer foco a los ").Append(F2(cuando)).Append(" s\r\n");
            Mira(la == 0 && lh >= 2 && cuando > 0 && v.Nombre == "Grand Theft Auto VI", "la visita corta ya da el juego en marcha (\"" + v.Nombre + "\")");
        }

        // ---------------- 3. El modo guardado en el perfil nuevo de la consola ----------------
        static void Modo()
        {
            sb.Append("\r\n-- 3. Perfil nuevo de la consola con el modo guardado (games\\mode.txt de la copia) --\r\n");
            string f = Paths.ModeFile;
            if (f.IndexOf(@"\AppData\Local\ConsoleEnhancer", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf(@"\Downloads\", StringComparison.OrdinalIgnoreCase) >= 0)
            { Mira(false, "sin --datos= de una copia: no se toca " + f); return; }
            string antes = File.Exists(f) ? File.ReadAllText(f) : null;
            try
            {
                string[][] casos = { new[] { "30", "30" }, new[] { "120", "120" }, new[] { "60", "60" }, new[] { "basura", "60" } };
                foreach (string[] q in casos)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(f));
                    File.WriteAllText(f, q[0]);
                    GameProfile p = new GameProfile();
                    p.Name = "Consola"; p.Cap = false; p.Mode120 = false;   // (lo de partida antes de leer mode.txt)
                    TrayApp.ModoGuardadoPrueba(p);
                    string got = p.Mode120 ? "120" : p.Cap ? "30" : "60";
                    Mira(got == q[1], "mode.txt \"" + q[0] + "\": el perfil nuevo queda en el modo " + got);
                }
            }
            finally { if (antes != null) File.WriteAllText(f, antes); else File.Delete(f); }
        }

        // ---------------- 5. La etiqueta con el inicio a medio aparecer (fundido, desplazado, agrandado) ----------------
        // No hay capturas de la animacion: se fingen con las 8 de verdad del banco. Se mira si el menu se reconoce (DetectorMenu) y si la
        // etiqueta se lee (y que nunca se lea otro juego)
        static void Degradadas()
        {
            sb.Append("\r\n-- 5. La etiqueta del juego en foco con el inicio a medio aparecer (fingido con las capturas del banco) --\r\n");
            string banco = @"C:\Switch2Magpie\juegos\test_deteccion";
            if (!Directory.Exists(banco)) return;
            string[] fs = Directory.GetFiles(banco, "menu_ce-gta-pestanas-*.png");
            string[] nombres = { "tal cual", "fundido 70 %", "fundido 45 %", "fundido 30 %", "baja 12 px", "baja 30 px", "sube 12 px", "agrandado 4 %", "agrandado 10 %", "desenfocado" };
            int[] menu = new int[nombres.Length], leido = new int[nombres.Length], otro = new int[nombres.Length];
            foreach (string f in fs)
                using (Bitmap b = Carga(f))
                    for (int k = 0; k < nombres.Length; k++)
                        using (Bitmap d = Degrada(b, k))
                        {
                            DateTime now = DateTime.Now;
                            LecturaMenu l = DetectorMenu.Clasifica(d, now, Consolas.Ps5, false, false);
                            DetectorMenu.OlvidaZonas();
                            if (l.Vista == VistaMenu.Menu) menu[k]++;
                            string crudo; bool fuerte;
                            string n = JuegoConsola.LeeNombre(d, Consolas.Ps5, out crudo, out fuerte);
                            if (fuerte && n != null && GameProfiles.SameGame("PS5: " + n, "PS5: Grand Theft Auto VI")) leido[k]++;
                            else if (fuerte && n != null) otro[k]++;
                        }
            for (int k = 0; k < nombres.Length; k++)
                sb.Append("  ").Append(nombres[k]).Append(": menu ").Append(menu[k]).Append(" de ").Append(fs.Length).Append(", etiqueta GTA VI ")
                  .Append(leido[k]).Append(", otro nombre fuerte ").Append(otro[k]).Append("\r\n");
            int malos = 0; foreach (int o in otro) malos += o;
            Mira(malos == 0, "ninguna etiqueta fuerte de otro juego con el inicio degradado");
        }

        static Bitmap Degrada(Bitmap b, int k)
        {
            int w = b.Width, h = b.Height;
            Bitmap d = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(d))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                switch (k)
                {
                    case 0: g.DrawImage(b, 0, 0, w, h); break;
                    case 1: case 2: case 3:
                        {
                            float a = k == 1 ? 0.70f : k == 2 ? 0.45f : 0.30f;
                            ColorMatrix cm = new ColorMatrix(new float[][] { new float[] { a, 0, 0, 0, 0 }, new float[] { 0, a, 0, 0, 0 }, new float[] { 0, 0, a, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 } });
                            using (ImageAttributes ia = new ImageAttributes()) { ia.SetColorMatrix(cm); g.DrawImage(b, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel, ia); }
                            break;
                        }
                    case 4: g.DrawImage(b, 0, h * 12 / 720, w, h); break;
                    case 5: g.DrawImage(b, 0, h * 30 / 720, w, h); break;
                    case 6: g.DrawImage(b, 0, -h * 12 / 720, w, h); break;
                    case 7: case 8:
                        {
                            float s = k == 7 ? 1.04f : 1.10f;
                            int nw = (int)(w * s), nh = (int)(h * s);
                            g.DrawImage(b, (w - nw) / 2, (h - nh) / 2, nw, nh);
                            break;
                        }
                    case 9:
                        using (Bitmap p = new Bitmap(w / 3, h / 3))
                        {
                            using (Graphics gp = Graphics.FromImage(p)) { gp.InterpolationMode = InterpolationMode.HighQualityBilinear; gp.DrawImage(b, 0, 0, w / 3, h / 3); }
                            g.DrawImage(p, 0, 0, w, h);
                        }
                        break;
                }
            }
            return d;
        }

        // ---------------- 4. La huella del HUD ----------------
        // Bordes de la imagen en una rejilla de 64x36 celdas (a 320x180): una celda "con borde" si >= 20 % de sus puntos tienen gradiente
        // fuerte. Aprender: las celdas con borde en casi todas las capturas del juego (el HUD no se mueve; el escenario si), sin las lineas
        // enteras (las bandas negras de una escena de cine). Probar: de esas celdas, cuantas tienen borde ahora, descontando lo que daria el
        // azar con los bordes de esta imagen
        const int GW = 64, GH = 36;

        static bool[] Bordes(Bitmap full, out double densidad)
        {
            const int W = 320, H = 180;
            int[] l = new int[W * H];
            using (Bitmap s = new Bitmap(W, H, PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(s))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(full, new Rectangle(0, 0, W, H), 0, 0, full.Width, full.Height, GraphicsUnit.Pixel);
                }
                BitmapData d = s.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    byte[] row = new byte[W * 4];
                    for (int y = 0; y < H; y++)
                    {
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, row.Length);
                        for (int x = 0; x < W; x++) { int o = x * 4; l[y * W + x] = (row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8; }
                    }
                }
                finally { s.UnlockBits(d); }
            }
            int[] cnt = new int[GW * GH];
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    int gx = l[y * W + x + 1] - l[y * W + x - 1], gy = l[(y + 1) * W + x] - l[(y - 1) * W + x];
                    if (Math.Abs(gx) + Math.Abs(gy) >= 48) cnt[(y * GH / H) * GW + x * GW / W]++;
                }
            bool[] e = new bool[GW * GH];
            int n = 0;
            int porCelda = (W / GW) * (H / GH);
            for (int i = 0; i < e.Length; i++) { e[i] = cnt[i] * 5 >= porCelda; if (e[i]) n++; }
            densidad = (double)n / e.Length;
            return e;
        }

        static bool[] Aprende(List<bool[]> frames)
        {
            bool[] m = new bool[GW * GH];
            if (frames.Count == 0) return m;
            for (int i = 0; i < m.Length; i++)
            {
                int c = 0;
                foreach (bool[] f in frames) if (f[i]) c++;
                m[i] = c * 10 >= frames.Count * 9;
            }
            // sin lineas enteras (bandas de cine, bordes de la imagen): una fila o columna con mas de la mitad de celdas marcadas
            for (int y = 0; y < GH; y++)
            {
                int c = 0;
                for (int x = 0; x < GW; x++) if (m[y * GW + x]) c++;
                if (c * 2 > GW) for (int x = 0; x < GW; x++) m[y * GW + x] = false;
            }
            for (int x = 0; x < GW; x++)
            {
                int c = 0;
                for (int y = 0; y < GH; y++) if (m[y * GW + x]) c++;
                if (c * 2 > GH) for (int y = 0; y < GH; y++) m[y * GW + x] = false;
            }
            return m;
        }

        static int Cuenta(bool[] m) { int n = 0; foreach (bool b in m) if (b) n++; return n; }

        // Parecido de una imagen con una huella: (recuerdo - densidad) / (1 - densidad), 0 = como el azar, 1 = todas las celdas del HUD
        static double Puntua(bool[] m, bool[] e, double dens)
        {
            int n = 0, si = 0;
            for (int i = 0; i < m.Length; i++) if (m[i]) { n++; if (e[i]) si++; }
            if (n == 0) return 0;
            double rec = (double)si / n;
            return dens >= 0.999 ? 0 : (rec - dens) / (1 - dens);
        }

        sealed class Cap { public string Juego, Sesion, Nombre; public bool[] E; public double Dens; }

        static void Hud(string dir)
        {
            sb.Append("\r\n-- 4. Huella del HUD (reconocer el juego sin el menu) con capturas de verdad: " + dir + " --\r\n");
            List<Cap> caps = new List<Cap>();
            List<string> files = new List<string>();
            foreach (string ext in new[] { "*.png", "*.jpg" }) files.AddRange(Directory.GetFiles(dir, ext));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string[] parts = Path.GetFileNameWithoutExtension(f).Split(new[] { "__" }, StringSplitOptions.None);
                if (parts.Length < 3) continue;
                Cap c = new Cap();
                c.Juego = parts[0]; c.Sesion = parts[1]; c.Nombre = parts[2];
                using (Bitmap b = Carga(f)) c.E = Bordes(b, out c.Dens);
                caps.Add(c);
            }
            // Una huella por juego y sesion (con las capturas de "juego" de esa sesion: las que llevan "hud" en el nombre)
            Dictionary<string, bool[]> huellas = new Dictionary<string, bool[]>();
            Dictionary<string, int> nAprende = new Dictionary<string, int>();
            foreach (Cap c in caps)
            {
                if (c.Juego == "otro" || c.Nombre.IndexOf("hud", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string k = c.Juego + "|" + c.Sesion;
                if (!nAprende.ContainsKey(k)) nAprende[k] = 0;
                nAprende[k]++;
            }
            foreach (string k in nAprende.Keys)
            {
                if (nAprende[k] < 2) { sb.Append("  huella ").Append(k).Append(": 1 sola captura con HUD, sin huella\r\n"); continue; }
                List<bool[]> fr = new List<bool[]>();
                foreach (Cap c in caps) if (c.Juego + "|" + c.Sesion == k && c.Nombre.IndexOf("hud", StringComparison.OrdinalIgnoreCase) >= 0) fr.Add(c.E);
                huellas[k] = Aprende(fr);
                sb.Append("  huella ").Append(k).Append(": ").Append(fr.Count).Append(" capturas, ").Append(Cuenta(huellas[k])).Append(" celdas del HUD\r\n");
            }
            // Probar cada captura con la huella de cada juego aprendida con sus OTRAS capturas con HUD (dejando fuera la que se prueba; hacen
            // falta 2 o mas). Con una sola sesion por juego es lo que hay (las de PERFILES en vuelta\ eran copias de estas: fuera)
            int aciertos = 0, falsos = 0, nose = 0, noseHud = 0, bienNose = 0;
            const double Umbral = 0.75, Margen = 0.40;
            foreach (Cap c in caps)
            {
                string mejor = null; double pm = -9, p2 = -9;
                Dictionary<string, double> porJuego = new Dictionary<string, double>();
                Dictionary<string, List<bool[]>> otras = new Dictionary<string, List<bool[]>>();
                foreach (Cap o in caps)
                {
                    if (o == c || o.Juego == "otro" || o.Nombre.IndexOf("hud", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!otras.ContainsKey(o.Juego)) otras[o.Juego] = new List<bool[]>();
                    otras[o.Juego].Add(o.E);
                }
                foreach (KeyValuePair<string, List<bool[]>> g in otras)
                {
                    if (g.Value.Count < 2) continue;
                    bool[] h = Aprende(g.Value);
                    if (Cuenta(h) < 8) continue;
                    porJuego[g.Key] = Puntua(h, c.E, c.Dens);
                }
                foreach (KeyValuePair<string, double> kv in porJuego)
                {
                    if (kv.Value > pm) { p2 = pm; pm = kv.Value; mejor = kv.Key; }
                    else if (kv.Value > p2) p2 = kv.Value;
                }
                bool decide = mejor != null && pm >= Umbral && (pm - Math.Max(0, p2)) >= Margen;
                string res;
                bool conHud = c.Nombre.IndexOf("hud", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!decide) { res = "no se sabe"; nose++; if (conHud && c.Juego != "otro") noseHud++; else bienNose++; }
                else if (mejor == c.Juego) { res = "ACIERTO " + mejor; aciertos++; }
                else { res = "FALLO (dice " + mejor + ")"; falsos++; }
                sb.Append("    ").Append(c.Juego).Append(" / ").Append(c.Sesion).Append(" / ").Append(c.Nombre).Append(": ").Append(res)
                  .Append(" (mejor ").Append(mejor ?? "-").Append(" ").Append(F2(pm)).Append(", segundo ").Append(F2(p2)).Append(", bordes ").Append(F2(c.Dens)).Append(")\r\n");
            }
            sb.Append("  Resultado: ").Append(aciertos).Append(" aciertos, ").Append(falsos).Append(" fallos (juego equivocado), ").Append(nose)
              .Append(" no se sabe (").Append(noseHud).Append(" con HUD a la vista, ").Append(bienNose).Append(" sin HUD u otra cosa)\r\n");
            Mira(falsos == 0, "huella del HUD: ningun juego equivocado con estas capturas");
        }
    }

    partial class TrayApp
    {
        internal static bool ModoGuardadoPrueba(GameProfile p) { return ModoGuardado(p); }
    }
}
