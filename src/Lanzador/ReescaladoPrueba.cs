// ReescaladoPrueba.cs - 22-11-2026 (1.2.2, agente REESCALADO): la config de Magpie que sale para cada combinacion (captura, modo,
// generacion, pasadas de DLSS 5, modelo de DLSS SR) con el ini de datos, sin consola ni grafica:
//   ConsoleEnhancer.exe [--datos=<carpeta>] --test-reescalado[=<informe>]   (de serie data\games\test_reescalado.txt; salida = fallos)
// Comprueba lo de src\Lanzador\Reescalado.cs: antiparpadeo de DLSS 5 con 1 pasada en el modo 30 (dlss5_antiparpadeo_30_1pasada), el
// enfoque CAS tras DLSS SR con los modelos transformer (magpie_nitidez_transformer) y que la escala automatica cuente el antiparpadeo.
// C# 5
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ConsoleEnhancer
{
    static class ReescaladoPrueba
    {
        static StringBuilder sb;
        static int fallos, pruebas;

        static void Ok(bool cond, string que)
        {
            pruebas++;
            if (!cond) fallos++;
            sb.AppendLine((cond ? "  ok    " : "  FALLO ") + que);
        }

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
        static string N(double v) { return double.IsNaN(v) ? "-" : v.ToString("0.###", CultureInfo.InvariantCulture); }

        public static int Run(string informe)
        {
            sb = new StringBuilder();
            fallos = pruebas = 0;
            sb.AppendLine("ConsoleEnhancer " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " - prueba de la config de reescalado y DLSS 5 (src\\Lanzador\\Reescalado.cs)");
            sb.AppendLine("ini: " + Paths.IniFile);
            string tmp = Path.Combine(Path.GetTempPath(), "ce_test_reescalado_" + DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture) + ".txt");
            bool w1080 = Config.Window1080;
            Forzar("dlss5State", 1);   // (como si estuviera nvngx_dlssnr.dll en magpie\)
            Forzar("dlssgState", 1);   // (y runtimes\dlssg_sm86)
            EscalaAuto.SalidaPrueba(3840, 2160);
            try
            {
                EscalaAuto.Reiniciar(tmp);
                Config cfg = Config.Load();
                sb.AppendLine("dlss5_antiparpadeo_30_1pasada=" + cfg.Af30OnePass + " dlss5_antiparpadeo=" + cfg.Dlss5AntiFlicker + " magpie_nitidez_transformer=" +
                              N(cfg.MagpieSharpenTransformer) + " upscale_sharpen=" + N(cfg.UpscaleSharpen) + "/" + N(cfg.UpscaleSharpen1080) + "/" + N(cfg.UpscaleSharpen720) +
                              " ventana_1080=" + (Config.Window1080 ? 1 : 0));
                sb.AppendLine();
                sb.AppendLine("captura modo gen pasadas modelo | DLSSNR: % antiparpadeo flujo5 | DLSS SR: modelo flujo calidad | CAS | perfil");
                foreach (string cap in new[] { "3840x2160", "2560x1440", "1920x1080" })
                    foreach (int modo in new[] { 30, 60 })
                        foreach (bool fg in new[] { true, false })
                            foreach (int pas in new[] { 1, 2, 3 })
                                foreach (int modelo in new[] { 5, 11, 13 })
                                {
                                    if (cap == "3840x2160" && modelo != 5) continue;   // (a 4K no hay escalado)
                                    EscalaAuto.Reiniciar(tmp);
                                    cfg.VideoSize = cap; cfg.CapMode = modo == 30; cfg.Mode120 = false; cfg.FgOff = !fg; cfg.Dlss5Off = false;
                                    cfg.Passes30 = pas; cfg.DlssModel = modelo; cfg.DlssModel30 = 0; cfg.Level30 = 3; cfg.ScalerPick = ""; cfg.AltScalerOn = false;
                                    List<object> fx = MagpieConfig.Chain(cfg, double.NaN);
                                    Dictionary<string, object> nr = Efecto(fx, "DLSSNR\\DLSSNR_AI_Filter"), sr = Efecto(fx, "DLSS\\DLSS_SR"), cas = Efecto(fx, "CAS\\CAS");
                                    int pasReal = nr != null ? (int)Num(nr, "multiPass") : 0;
                                    sb.AppendLine("  " + cap + " " + modo + (fg ? " fg " : " -- ") + pas + " " + modelo + " | " +
                                                  (nr != null ? N(Num(nr, "inputResolutionPercent")) + " % af " + N(Num(nr, "antiFlicker")) + " flujo " + N(Num(nr, "opticalFlowMethod")) + " pasadas " + pasReal : "sin DLSSNR") + " | " +
                                                  (sr != null ? N(Num(sr, "renderPreset")) + " flujo " + N(Num(sr, "opticalFlowMethod")) + " q" + N(Num(sr, "nvidiaOpticalFlowQuality")) : "sin DLSS SR") + " | " +
                                                  (cas != null ? N(Num(cas, "sharpness")) : "-") + " | " + cfg.ProfileText);
                                    if (nr == null) continue;
                                    // 5) 2-3 pasadas: Chroma Strength del residuo, con la captura 4K dlss5_pasadas_color_4k
                                    if (pasReal >= 2)
                                    {
                                        double cEsp = cap == "3840x2160" && cfg.Dlss5MultiChroma4k >= 0 ? cfg.Dlss5MultiChroma4k : cfg.Dlss5MultiChroma;
                                        double cVis = nr.ContainsKey("residualSaturation") ? Num(nr, "residualSaturation") : 1;
                                        Ok(Math.Abs(cVis - cEsp) < 0.002, cap + " modo " + modo + (fg ? " fg" : "") + " p" + pasReal + ": color de las pasadas " + N(cVis) + " = " + N(cEsp) +
                                           (nr.ContainsKey("residualDetailGain") ? ", detalle " + N(Num(nr, "residualDetailGain")) : "") +
                                           (nr.ContainsKey("residualLocalCompression") ? ", contener " + N(Num(nr, "residualLocalCompression")) : ""));
                                    }
                                    // 1) modo 30: el antiparpadeo tambien con 1 pasada (el de dlss5_antiparpadeo_30_1pasada) y con 2-3 el de siempre
                                    if (modo == 30 && pasReal == 1 && cfg.Af30OnePass >= 0)
                                        Ok((int)Num(nr, "antiFlicker") == Math.Min(4, cfg.Af30OnePass), cap + " modo 30 1 pasada: antiparpadeo " + N(Num(nr, "antiFlicker")) + " = dlss5_antiparpadeo_30_1pasada " + cfg.Af30OnePass);
                                    // 2) CAS tras DLSS SR: x magpie_nitidez_transformer con J/K/L/M, igual con E
                                    if (sr != null)
                                    {
                                        // 4) flujo de DLSS SR: con L/M el de DLSS 5 si el modo dice ninguno (dlss_flujo_transformer)
                                        int fm = cfg.DlssFlowNow, f5 = cfg.Dlss5FlowNow, mod = (int)Num(sr, "renderPreset");
                                        int fEsp = fm == 0 && f5 > 0 && cfg.SrFlowTransformer2 > 0 && (mod == 12 || mod == 13) ? f5 : fm;
                                        Ok((int)Num(sr, "opticalFlowMethod") == fEsp, cap + " modo " + modo + (fg ? " fg" : "") + " p" + pasReal + " modelo " + mod + ": flujo de DLSS SR " + N(Num(sr, "opticalFlowMethod")) +
                                           " (el del modo " + fm + ", DLSS 5 " + f5 + ")");
                                        double s0 = cfg.SharpenNow, k = Config.EsTransformer((int)Num(sr, "renderPreset")) ? cfg.MagpieSharpenTransformer : 1;
                                        double esperado = s0 * k;
                                        double visto = cas != null ? Num(cas, "sharpness") : 0;
                                        Ok(Math.Abs(visto - (esperado > 0.001 ? esperado : 0)) < 0.002, cap + " modo " + modo + (fg ? " fg" : "") + " p" + pasReal + " modelo " + N(Num(sr, "renderPreset")) + ": CAS " + N(visto) + " = " + N(s0) + " x " + N(k));
                                    }
                                }
                // 4) El motor: "sin reescalado" (catrom) sin enfoque x enfoque_sin_reescalado; con DLSS, el de siempre
                {
                    MethodInfo es = typeof(TrayApp).GetMethod("EngineSharpen", BindingFlags.NonPublic | BindingFlags.Static);
                    sb.AppendLine();
                    foreach (string pick in new[] { "ninguno", "" })
                        foreach (string cap in new[] { "1920x1080", "2560x1440" })
                        {
                            cfg.VideoSize = cap; cfg.Dlss5Off = true; cfg.ScalerPick = pick; cfg.AltScalerOn = false;
                            double v = es != null ? (double)es.Invoke(null, new object[] { cfg }) : double.NaN;
                            string eu = Ce.EngineUpscaler(cfg);
                            double s0 = cap == "1920x1080" ? cfg.UpscaleSharpen1080 : cfg.UpscaleSharpen;
                            string p = cfg.EnginePresetNow;
                            double kt = p == "J" || p == "K" || p == "L" || p == "M" ? cfg.EngineSharpenTransformer : 1;
                            double esperado = s0 * kt * (eu == "catrom" ? cfg.SharpenNoScale : 1);
                            Ok(Math.Abs(v - esperado) < 0.002, "motor " + cap + " " + eu + " (modelo " + p + "): enfoque " + N(v) + " = " + N(esperado));
                        }
                    cfg.ScalerPick = ""; cfg.Dlss5Off = false;
                }
                // 3) La escala automatica cuenta el antiparpadeo nuevo (1 pasada, modo 30): con el, el mismo % o menos que sin el
                sb.AppendLine();
                foreach (string cap in new[] { "3840x2160", "2560x1440", "1920x1080" })
                {
                    EscalaAuto.Reiniciar(tmp);
                    cfg.VideoSize = cap; cfg.CapMode = true; cfg.FgOff = false; cfg.Passes30 = 1; cfg.DlssModel = 5; cfg.Dlss5Off = false;
                    CadenaEscala c = cfg.CadenaEscalaPara(1);
                    CadenaEscala sinAf = c.Copia(); sinAf.AfUna = false;
                    int con = EscalaAuto.Mejor(c, 1, 1.0, 0), sin = EscalaAuto.Mejor(sinAf, 1, 1.0, 0);
                    double ms = EscalaAuto.Prevision(c, 50, 1, 1.0, 0) - EscalaAuto.Prevision(sinAf, 50, 1, 1.0, 0);
                    Ok(!c.AfUna || con <= sin, cap + " 30 + FG 1 pasada: % con antiparpadeo " + con + " (sin el " + sin + "), +" + ms.ToString("0.00", CultureInfo.InvariantCulture) + " ms previstos");
                }
            }
            catch (Exception ex) { fallos++; sb.AppendLine("EXCEPCION: " + ex); }
            finally
            {
                Config.Window1080 = w1080;
                Forzar("dlss5State", 0); Forzar("dlssgState", 0);
                EscalaAuto.Reiniciar(null);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            sb.AppendLine();
            sb.AppendLine(pruebas + " comprobaciones, " + fallos + " fallo(s)");
            string f = string.IsNullOrEmpty(informe) ? Path.Combine(Paths.Datos, @"games\test_reescalado.txt") : informe;
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(f))); File.WriteAllText(f, sb.ToString(), new UTF8Encoding(false)); } catch { }
            return fallos;
        }
    }
}
