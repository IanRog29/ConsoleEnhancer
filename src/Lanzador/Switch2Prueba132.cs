// Switch2Prueba132.cs - 1.3.2 (agente SWITCH2): la parte 12 de --test-switch2 (src\Lanzador\Switch2Datos.cs), sin consola ni grafica:
// 12. Jugando, la vuelta al HOME (SalidaHome.cs): capturas reales del HOME de la Switch 2 tal como las da el motor jugando (la captura del
//     juego a 1080p / 1440p / 4K y la ligera de 1 de cada 2-3 puntos) se ven como el HOME, tambien SIN el nombre cian (tapada su fila);
//     el HOME atenuado, el negro y las capturas de juegos (Switch 2 y PS5, test_deteccion) nunca por sus franjas. Con home_por_franjas=0
//     (la 1.3.1) el HOME sin el nombre no se veia.
// C# 5
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        static void Switch2Prueba132(StringBuilder sb, Action<string, bool> check, string datosUsuario, string viejo)
        {
            string refFile = datosUsuario != null ? Path.Combine(datosUsuario, @"games\home_ref.txt") : null;
            string envRef = Environment.GetEnvironmentVariable("CE_HOME_REF");
            if (!string.IsNullOrEmpty(envRef)) refFile = envRef;
            double[] rf = LeeRefPrueba(refFile);
            if (rf == null) { sb.Append("      12. (sin games\\home_ref.txt en estos datos: sin la parte 12)\r\n"); return; }
            // El HOME (capturas de 1280x720: las de debug\ps5 del HOME con la PS5 puesta y la de la 1.3; CE_BANCO_HOME = mas)
            List<string> homes = new List<string>();
            if (datosUsuario != null)
                foreach (string n in new[] { "20261123-180816_1280x720.png", "20261123-180818_1280x720.png", "20261123-202244_1280x720.png" })
                    if (File.Exists(Path.Combine(datosUsuario, @"games\debug\ps5\" + n))) homes.Add(Path.Combine(datosUsuario, @"games\debug\ps5\" + n));
            string envHome = Environment.GetEnvironmentVariable("CE_BANCO_HOME");
            if (!string.IsNullOrEmpty(envHome) && Directory.Exists(envHome))
                foreach (string f in Directory.GetFiles(envHome, "*.png")) if (!homes.Contains(f)) homes.Add(f);
            int[][] tams = new int[][] { new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 } };
            int vistos = 0, n0 = 0, sinNombreSi = 0, sinNombreN = 0, sinNombreAntes = 0, atenuadoMal = 0;
            double hdMax = 0;
            foreach (string f in homes)
            {
                using (Bitmap src = Switch2Datos.Cargar32(f))
                {
                    bool nativa = src.Width < 1000;   // (las de 640x360 de test_biblioteca: tal cual)
                    foreach (int[] t in nativa ? new int[][] { new[] { src.Width, src.Height } } : tams)
                    {
                        using (Bitmap full = nativa ? new Bitmap(src) : LigeraPrueba(src, t[0], t[1]))
                        {
                            double hd;
                            string d = SalidaHomeReglas.Decide(full, rf, true, out hd);
                            n0++;
                            if (d.Length > 0) vistos++;
                            hdMax = Math.Max(hdMax, hd);
                            sb.Append("      12. ").Append(Path.GetFileName(f)).Append(" captura ").Append(t[0]).Append("x").Append(t[1]).Append(" (ligera ").Append(full.Width).Append("x").Append(full.Height)
                              .Append("): ").Append(d.Length > 0 ? d : "NADA").Append(", franjas a ").Append(hd.ToString("0.0", CultureInfo.InvariantCulture));
                            // Sin el nombre: su fila tapada con el fondo del HOME
                            using (Bitmap tapada = new Bitmap(full))
                            {
                                using (Graphics g = Graphics.FromImage(tapada))
                                using (SolidBrush b = new SolidBrush(tapada.GetPixel(4, tapada.Height * 40 / 360)))
                                    g.FillRectangle(b, 0, tapada.Height * 66 / 360, tapada.Width, tapada.Height * 34 / 360);
                                double hd2;
                                string d2 = SalidaHomeReglas.Decide(tapada, rf, true, out hd2);
                                string d2a = SalidaHomeReglas.Decide(tapada, rf, false, out hd2);
                                sinNombreN++;
                                if (d2 == "franjas") sinNombreSi++;
                                if (d2a.Length > 0) sinNombreAntes++;
                                sb.Append("; sin el nombre: ").Append(d2.Length > 0 ? d2 : "NADA").Append(" (la 1.3.1: ").Append(d2a.Length > 0 ? d2a : "nada").Append(")");
                            }
                            // Atenuado (un aviso encima): nunca el HOME
                            using (Bitmap aten = Oscurece(full, 0.45))
                            {
                                double hd3;
                                string d3 = SalidaHomeReglas.Decide(aten, rf, true, out hd3);
                                if (d3.Length > 0) atenuadoMal++;
                                sb.Append("; atenuado: ").Append(d3.Length > 0 ? d3 : "nada").Append("\r\n");
                            }
                        }
                    }
                }
            }
            if (n0 > 0)
            {
                check("12. El HOME jugando (" + n0 + " capturas a los tamaños del juego): siempre visto (" + vistos + "), franjas a " + hdMax.ToString("0.0", CultureInfo.InvariantCulture) + " como mucho", vistos == n0);
                check("12. El HOME sin el nombre cian (" + sinNombreN + "): visto por sus franjas en " + sinNombreSi + " (la 1.3.1, en " + sinNombreAntes + ")", sinNombreSi == sinNombreN && sinNombreAntes == 0);
                check("12. El HOME atenuado (un aviso encima): nunca el HOME (" + atenuadoMal + ")", atenuadoMal == 0);
            }
            else sb.Append("      12. (sin capturas del HOME en estos datos)\r\n");
            // Juegos (Switch 2 y PS5) y negro: nunca por las franjas
            List<string> juegos = new List<string>();
            foreach (string dir in new[] { viejo != null ? Path.Combine(viejo, "test_deteccion") : null, Environment.GetEnvironmentVariable("CE_BANCO_DETECCION") })
                if (dir != null && Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*.png"))
                        if (Path.GetFileName(f).StartsWith("juego_", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("menu_ce-gta", StringComparison.Ordinal)) juegos.Add(f);
            if (datosUsuario != null && Directory.Exists(Path.Combine(datosUsuario, @"games\debug\ps5")))
                foreach (string f in Directory.GetFiles(Path.Combine(datosUsuario, @"games\debug\ps5"), "*.png"))
                    if (!homes.Contains(f) && !Path.GetFileName(f).StartsWith("20261123-18", StringComparison.Ordinal)) juegos.Add(f);
            int jn = 0, jMal = 0;
            double hdMin = 999;
            foreach (string f in juegos)
            {
                using (Bitmap src = Switch2Datos.Cargar32(f))
                foreach (int[] t in tams)
                {
                    using (Bitmap full = LigeraPrueba(src, t[0], t[1]))
                    {
                        double hd;
                        string d = SalidaHomeReglas.Decide(full, rf, true, out hd);
                        jn++;
                        hdMin = Math.Min(hdMin, hd);
                        if (d == "franjas") { jMal++; sb.Append("      12. MAL: ").Append(Path.GetFileName(f)).Append(" ").Append(t[0]).Append("x").Append(t[1]).Append(" por sus franjas (").Append(hd.ToString("0.0", CultureInfo.InvariantCulture)).Append(")\r\n"); }
                    }
                    break;   // (un tamaño basta para los juegos: las franjas son medias de bloques)
                }
            }
            using (Bitmap negro = new Bitmap(1280, 720, PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(negro)) g.Clear(Color.FromArgb(3, 3, 3));
                double hd;
                if (SalidaHomeReglas.Decide(negro, rf, true, out hd) == "franjas") jMal++;
                jn++;
            }
            check("12. Juegos de la Switch 2 / la PS5 y negro (" + jn + "): ninguno es el HOME por sus franjas (" + jMal + "; las franjas mas parecidas a " + hdMin.ToString("0.0", CultureInfo.InvariantCulture) + ", el limite 6)", jMal == 0 && jn > 1);
        }

        static double[] LeeRefPrueba(string f)
        {
            try
            {
                if (f == null || !File.Exists(f)) return null;
                string[] p = File.ReadAllText(f).Split(new[] { ',', ';', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                double[] r = new double[p.Length];
                for (int i = 0; i < p.Length; i++) r[i] = double.Parse(p[i], CultureInfo.InvariantCulture);
                return r.Length > 10 ? r : null;
            }
            catch { return null; }
        }

        // La captura del juego a w x h (bilineal desde la de prueba) y la ligera del motor: 1 de cada round(w / 1280) puntos, sin filtro
        static Bitmap LigeraPrueba(Bitmap src, int w, int h)
        {
            using (Bitmap cap = new Bitmap(w, h, PixelFormat.Format32bppRgb))
            {
                using (Graphics g = Graphics.FromImage(cap))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(src, 0, 0, w, h);
                }
                int step = Math.Max(1, (int)Math.Round(w / 1280.0));
                int ow = w / step, oh = h / step;
                Bitmap o = new Bitmap(ow, oh, PixelFormat.Format32bppRgb);
                BitmapData a = cap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                BitmapData b = o.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    byte[] row = new byte[a.Stride], orow = new byte[b.Stride];
                    for (int y = 0; y < oh; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(new IntPtr(a.Scan0.ToInt64() + (long)y * step * a.Stride), row, 0, a.Stride);
                        for (int x = 0; x < ow; x++) Buffer.BlockCopy(row, x * step * 4, orow, x * 4, 4);
                        System.Runtime.InteropServices.Marshal.Copy(orow, 0, new IntPtr(b.Scan0.ToInt64() + (long)y * b.Stride), b.Stride);
                    }
                }
                finally { cap.UnlockBits(a); o.UnlockBits(b); }
                return o;
            }
        }
    }
}
