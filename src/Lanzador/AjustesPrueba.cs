// AjustesPrueba.cs - 23-11-2026 (1.3.1, octava ronda, agente AJUSTES). C# 5
//
// --test-ajustes[=<informe>][;<carpeta de trabajo>[;<data de una version>[;<copia de %LOCALAPPDATA%\ConsoleEnhancer>]]]: la carpeta de los
// ajustes en el PC (AjustesPc.cs) sobre COPIAS, sin tocar nada de verdad (todo dentro de la carpeta de trabajo; sin ella, una en %TEMP%):
//  1. version nueva descomprimida en una carpeta suelta (sin versiones al lado): trae TAL CUAL lo de la copia de seguridad (backup\)
//  2. version nueva al lado de la de antes (Descargas\ConsoleEnhancer-v1.3 con su data\): trae lo de esa (no lo de la copia, que va por detras)
//  3. la segunda vez y otra version nueva despues: sin migrar otra vez, lo de la carpeta del PC tal cual; el sello sube
//  4. una version MAS ANTIGUA abierta despues: su copia (settings-older\<version>), la carpeta del PC sin tocar ni un byte
//  5. portable.txt: el que traian los zip no cuenta; uno del usuario si
//  6. la carpeta del PC que no se puede escribir: null (la app va a data\ junto al exe)
//  7. lo que ya no existe: perfiles y lo elegido con XeSS FG / FSR 3 FG -> el mas parecido (Smooth Motion en NVIDIA), lo demas igual
//  8. las versiones (1.3 < 1.3.1 < 1.3.10) y que es una prueba
// Sin <data de una version> ni la copia de %LOCALAPPDATA%: unos datos de ejemplo. Devuelve los fallos (0 = bien).
// --ajustes-donde[=<informe>]: con la logica de verdad (para probar la app entera con CE_PC_DIR=<carpeta>): donde van los ajustes y lo que hizo.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ConsoleEnhancer
{
    static class AjustesPrueba
    {
        static StringBuilder sb;
        static int fallos, n;

        static void Ok(bool ok, string que)
        {
            n++;
            if (!ok) fallos++;
            sb.AppendLine((ok ? "bien  " : "FALLO ") + que);
        }

        public static int Run(string arg)
        {
            sb = new StringBuilder("# AJUSTES: la carpeta de los ajustes en el PC (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")\r\n");
            fallos = 0; n = 0;
            string[] pa = (arg ?? "").Split(';');
            string informe = pa.Length > 0 && pa[0].Length > 0 ? pa[0] : null;
            string work = pa.Length > 1 && pa[1].Length > 0 ? Path.GetFullPath(pa[1]) : Path.Combine(Path.GetTempPath(), "ce_test_ajustes_" + System.Diagnostics.Process.GetCurrentProcess().Id);
            string dataVer = pa.Length > 2 && pa[2].Length > 0 ? Path.GetFullPath(pa[2]) : null;
            string pcCopia = pa.Length > 3 && pa[3].Length > 0 ? Path.GetFullPath(pa[3]) : null;
            bool xeAntes = Config.XessFgOfrecer, fsAntes = Config.FsrFgOfrecer;
            string envez = Config.GenEnVezDeDlss;
            try
            {
                // nunca dentro de los datos de verdad
                string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConsoleEnhancer");
                if (Dentro(work, real) || Dentro(work, Paths.Datos)) { sb.AppendLine("FALLO la carpeta de trabajo esta dentro de los datos de verdad: " + work); fallos++; return Fin(informe); }
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                string fuenteData = Path.Combine(work, "_fuente_data"), fuentePc = Path.Combine(work, "_fuente_pc");
                if (dataVer != null && Directory.Exists(dataVer)) CopiarArbol(dataVer, fuenteData, true); else Ejemplo(fuenteData, "ps5", DateTime.Now.AddMinutes(-10));
                if (pcCopia != null && Directory.Exists(pcCopia)) CopiarArbol(pcCopia, fuentePc, true);
                else { Ejemplo(Path.Combine(fuentePc, "backup"), "switch2", DateTime.Now.AddMinutes(-12)); Ejemplo(fuentePc, "viejo", DateTime.Now.AddDays(-2)); }
                if (!File.Exists(Path.Combine(fuentePc, @"backup\consoleenhancer.ini"))) Ejemplo(Path.Combine(fuentePc, "backup"), "switch2", DateTime.Now.AddMinutes(-12));
                sb.AppendLine("fuentes: data de una version " + (dataVer ?? "(ejemplo)") + "; %LOCALAPPDATA%\\ConsoleEnhancer " + (pcCopia ?? "(ejemplo)") + "; trabajo " + work);

                // 1) suelta: Escritorio\Cosas\ConsoleEnhancer-v1.3.1, sin nada al lado -> lo de backup\, tal cual
                {
                    string pc = Path.Combine(work, "pc1");
                    CopiarArbol(fuentePc, pc, true);
                    string app = Path.Combine(work, @"Escritorio\Cosas\ConsoleEnhancer-v1.3.1");
                    Directory.CreateDirectory(app);
                    string d = AjustesPc.ElegirPc(app, pc, "1.3.1", false);
                    Ok(d != null && Norm(d) == Norm(Path.Combine(pc, "settings")), "1 suelta: carpeta " + d);
                    Igual("1 suelta", Path.Combine(pc, "backup"), d);
                    Ok(AjustesPc.LeerSello(d) == "1.3.1", "1 suelta: sello " + AjustesPc.LeerSello(d));
                    Ok(File.Exists(Path.Combine(d, Integrado.HeredadoTxt)), "1 suelta: " + Integrado.HeredadoTxt + " (" + PrimeraLinea(Path.Combine(d, Integrado.HeredadoTxt)) + ")");
                    Ok(Directory.GetFiles(Path.Combine(d, "games"), "test_*").Length == 0 && Directory.GetFiles(d, "*.antes-*").Length == 0, "1 suelta: sin informes de pruebas ni copias .antes-*");
                    // la cache de shaders tambien
                    string cs = Path.Combine(pc, @"backup\logs\cache");
                    if (Directory.Exists(cs)) Ok(Directory.GetFiles(Path.Combine(d, @"logs\cache"), "*.cso").Length >= Directory.GetFiles(cs, "*.cso").Length, "1 suelta: la cache de shaders (" + Directory.GetFiles(cs, "*.cso").Length + ")");
                }

                // 2) al lado: Descargas\ConsoleEnhancer-v1.3\data (la de antes, usada hace poco) + Descargas\ConsoleEnhancer-v1.3.1 -> lo de la 1.3
                string pc2 = Path.Combine(work, "pc2"), settings2 = null, app2 = Path.Combine(work, @"Descargas\ConsoleEnhancer-v1.3.1");
                {
                    CopiarArbol(fuentePc, pc2, true);
                    string vieja = Path.Combine(work, @"Descargas\ConsoleEnhancer-v1.3");
                    CopiarArbol(fuenteData, Path.Combine(vieja, "data"), true);
                    File.WriteAllText(Path.Combine(vieja, "portable.txt"), "ConsoleEnhancer portable: " + AjustesPc.PortableViejo + "\\ folder next to it.\r\n");
                    // (la copia de seguridad va hasta 2 min por detras: la de la version de al lado es la buena)
                    Toca(Path.Combine(vieja, "data"), DateTime.Now.AddMinutes(-1));
                    Toca(Path.Combine(pc2, "backup"), DateTime.Now.AddMinutes(-2));
                    Directory.CreateDirectory(app2);
                    settings2 = AjustesPc.ElegirPc(app2, pc2, "1.3.1", false);
                    Igual("2 al lado", Path.Combine(vieja, "data"), settings2);
                    string c1 = Leer(Path.Combine(vieja, @"data\games\console.txt")), c2 = Leer(Path.Combine(settings2, @"games\console.txt"));
                    Ok(c1 == c2, "2 al lado: games\\console.txt de la 1.3 (\"" + c1 + "\"), no el de la copia (\"" + Leer(Path.Combine(pc2, @"backup\games\console.txt")) + "\") -> \"" + c2 + "\"");
                    // y si la copia de seguridad es bastante mas nueva (se uso una version que no esta al lado), la copia
                    string pc2b = Path.Combine(work, "pc2b"), app2b = Path.Combine(work, @"Descargas2\ConsoleEnhancer-v1.3.1");
                    CopiarArbol(fuentePc, pc2b, true);
                    CopiarArbol(fuenteData, Path.Combine(work, @"Descargas2\ConsoleEnhancer-v1.3\data"), true);
                    Toca(Path.Combine(work, @"Descargas2\ConsoleEnhancer-v1.3\data"), DateTime.Now.AddHours(-5));
                    Toca(Path.Combine(pc2b, "backup"), DateTime.Now.AddMinutes(-1));
                    Directory.CreateDirectory(app2b);
                    string s2b = AjustesPc.ElegirPc(app2b, pc2b, "1.3.1", false);
                    Igual("2b la copia mas nueva", Path.Combine(pc2b, "backup"), s2b);
                }

                // 3) la segunda vez (sin migrar otra vez) y una version nueva despues (sello arriba, lo mismo)
                {
                    string pf = Path.Combine(settings2, @"games\profiles.txt");
                    File.AppendAllText(pf, "Juego de prueba|1920x1080|2|1|1|1|60|1|1|smooth,\r\n", new UTF8Encoding(false));
                    string h = Huella(settings2);
                    string d = AjustesPc.ElegirPc(app2, pc2, "1.3.1", false);
                    Ok(Norm(d) == Norm(settings2) && Huella(settings2) == h, "3 segunda vez: la misma carpeta y sin tocar (lo cambiado se queda)");
                    string app3 = Path.Combine(work, @"OtraUnidad\CE\ConsoleEnhancer-v1.3.2");
                    Directory.CreateDirectory(app3);
                    d = AjustesPc.ElegirPc(app3, pc2, "1.3.2", false);
                    Ok(Norm(d) == Norm(settings2) && AjustesPc.LeerSello(d) == "1.3.2" && Leer(pf).Contains("Juego de prueba"), "3 version nueva (1.3.2) en otra carpeta: la misma, sello " + AjustesPc.LeerSello(d) + ", con lo cambiado");
                }

                // 4) una version mas antigua (1.3.1) abierta despues de la 1.3.2: su copia; la del PC, ni un byte
                {
                    string h = Huella(settings2);
                    string d = AjustesPc.ElegirPc(app2, pc2, "1.3.1", false);
                    string copia = Path.Combine(pc2, @"settings-older\1.3.1");
                    Ok(Norm(d) == Norm(copia) && File.Exists(Path.Combine(copia, "consoleenhancer.ini")), "4 mas antigua: trabaja en " + d);
                    Ok(Huella(settings2) == h && AjustesPc.LeerSello(settings2) == "1.3.2", "4 mas antigua: la carpeta del PC sin tocar (sello " + AjustesPc.LeerSello(settings2) + ")");
                    // lo que cambie la antigua se queda en su copia; lo que cambie despues la nueva, le llega
                    string pfc = Path.Combine(copia, @"games\profiles.txt");
                    File.AppendAllText(pfc, "Solo en la vieja|1280x720|1|1|1|1|30|1|1\r\n", new UTF8Encoding(false));
                    File.SetLastWriteTime(pfc, DateTime.Now.AddMinutes(1));
                    string mt = Path.Combine(settings2, @"games\mode.txt");
                    File.WriteAllText(mt, "60", new UTF8Encoding(false)); File.SetLastWriteTime(mt, DateTime.Now.AddMinutes(2));
                    h = Huella(settings2);
                    d = AjustesPc.ElegirPc(app2, pc2, "1.3.1", false);
                    Ok(Leer(pfc).Contains("Solo en la vieja") && Leer(Path.Combine(copia, @"games\mode.txt")) == "60" && !Leer(Path.Combine(settings2, @"games\profiles.txt")).Contains("Solo en la vieja") && Huella(settings2) == h,
                       "4 mas antigua otra vez: lo suyo se queda en su copia, le llega lo nuevo de la 1.3.2 y la del PC sigue igual");
                    // y la 1.3.2 otra vez: la del PC, sin lo de la vieja
                    d = AjustesPc.ElegirPc(app2, pc2, "1.3.2", false);
                    Ok(Norm(d) == Norm(settings2) && Huella(settings2) == h, "4 la 1.3.2 otra vez: la del PC tal cual");
                    // los ayudantes (la espera...) de la antigua: la misma copia, sin copiar nada
                    string hc = Huella(copia);
                    File.SetLastWriteTime(mt, DateTime.Now.AddMinutes(3));
                    d = AjustesPc.ElegirPc(app2, pc2, "1.3.1", true);
                    Ok(Norm(d) == Norm(copia) && Huella(copia) == hc, "4 ayudante de la antigua: su copia, sin copiar");
                }

                // 5) portable.txt
                {
                    string app = Path.Combine(work, @"Portable\ConsoleEnhancer-v1.3.1");
                    Directory.CreateDirectory(app);
                    Ok(!AjustesPc.PortablePedido(app), "5 sin portable.txt: la carpeta del PC");
                    File.WriteAllText(Path.Combine(app, "portable.txt"), "﻿ConsoleEnhancer portable: " + AjustesPc.PortableViejo + "\\ folder next to it.\r\nDelete it to keep them in %LOCALAPPDATA%\\ConsoleEnhancer\r\n");
                    Ok(!AjustesPc.PortablePedido(app), "5 el portable.txt de los zip de antes: no cuenta");
                    File.WriteAllText(Path.Combine(app, "portable.txt"), "");
                    Ok(AjustesPc.PortablePedido(app), "5 un portable.txt del usuario (vacio): portable");
                    // portable pedido sin datos: lo de la carpeta del PC (la usada mas recientemente)
                    string dp = Path.Combine(app, "data");
                    Directory.CreateDirectory(dp);
                    Toca(settings2, DateTime.Now.AddMinutes(4));
                    AjustesPc.Migrar(dp, app, pc2);
                    Igual("5 portable sin datos", settings2, dp);
                }

                // 6) la carpeta del PC no se puede escribir (debajo de un fichero): null -> data\ junto al exe
                {
                    string f = Path.Combine(work, "soy_un_fichero.txt");
                    File.WriteAllText(f, "x");
                    string d = AjustesPc.ElegirPc(Path.Combine(work, @"X\ConsoleEnhancer-v1.3.1"), Path.Combine(f, "pc"), "1.3.1", false);
                    Ok(d == null, "6 no se puede escribir: " + (d ?? "null (va data\\ junto al exe)"));
                }

                // 7) lo que ya no existe: XeSS FG / FSR 3 FG -> Smooth Motion (esta grafica: NVIDIA), el resto igual
                {
                    Config.XessFgOfrecer = false; Config.FsrFgOfrecer = false; Config.GenEnVezDeDlss = "smooth";
                    string g = Path.Combine(work, @"quitados\games");
                    Directory.CreateDirectory(g);
                    string[] antes = {
                        "# Ajustes recordados por juego (los pone el mando): nombre|captura|nivel|pasadas|dlss5|escalador|modo|generacion|nombre_seguro|tipo_generacion_60,30",
                        "Consola|3840x2160|2|1|0|1|30|0|1|,efg",
                        "PS5: Grand Theft Auto VI|3840x2160|3|1|1|1|30|1|1|,xess",
                        "PS5: Bloodborne|1920x1080|4|2|1|1|30|1|1|xess,fsr3",
                        "Mario Kart World|2560x1440|2|1|1|xess|60|1|1|xess,",
                        "Rocket League|1920x1080|1|2|0|1|60|0|1",
                        "Viejo con multiplicador|2560x1440|2|1|1|1|30|1|1|dlss,xess,3,4",
                        "PS5: PLAYÑOOM|3840x2160|2|1|1|1|30|1|1|,smooth" };
                    string[] esperado = {
                        antes[0], antes[1],
                        "PS5: Grand Theft Auto VI|3840x2160|3|1|1|1|30|1|1|,smooth",
                        "PS5: Bloodborne|1920x1080|4|2|1|1|30|1|1|smooth,smooth",
                        "Mario Kart World|2560x1440|2|1|1|xess|60|1|1|smooth,",
                        antes[5],
                        "Viejo con multiplicador|2560x1440|2|1|1|1|30|1|1|smooth,smooth",
                        antes[7] };
                    File.WriteAllText(Path.Combine(g, "profiles.txt"), string.Join("\r\n", antes) + "\r\n", new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(g, "framegen_type.txt"), "60=xess\r\n30=efg\r\n", new UTF8Encoding(false));
                    Action<string> hookAntes = Log.Hook;
                    List<string> logs = new List<string>();
                    Log.Hook = delegate(string m) { lock (logs) logs.Add(m); };
                    int c;
                    try { c = AjustesPc.PonerAlDia(g, false); } finally { Log.Hook = hookAntes; }
                    string[] despues = File.ReadAllLines(Path.Combine(g, "profiles.txt"), Encoding.UTF8);
                    bool igual = despues.Length == esperado.Length;
                    for (int i = 0; igual && i < esperado.Length; i++) if (despues[i] != esperado[i]) { igual = false; sb.AppendLine("      linea " + i + ": \"" + despues[i] + "\" (esperado \"" + esperado[i] + "\")"); }
                    Ok(igual, "7 perfiles: XeSS FG / FSR 3 FG / DLSS FG -> Smooth Motion en la columna 10, el resto de cada linea igual (escalador XeSS SR de Mario Kart sigue)");
                    Ok(Leer(Path.Combine(g, "framegen_type.txt")).Replace("\r", "").Replace("\n", " ").Trim() == "60=smooth 30=efg", "7 framegen_type.txt: " + Leer(Path.Combine(g, "framegen_type.txt")).Replace("\r", "").Replace("\n", " ").Trim());
                    Ok(c == 7 && logs.Count == 7, "7 cambios " + c + ", al log " + logs.Count + (logs.Count > 0 ? " (p. ej. \"" + logs[0] + "\")" : ""));
                    Ok(AjustesPc.PonerAlDia(g, false) == 0, "7 la segunda vez: nada que cambiar");
                    // los perfiles se leen como la app: el tipo y lo demas
                    GameProfile gp = new GameProfile(); gp.ParsePick("xess,fsr3");
                    Ok(gp.Pick60 == "smooth" && gp.Pick30 == "smooth", "7 un perfil con xess,fsr3 que llegue de otra parte, al leerlo: " + gp.PickText);
                    // con generacion_xess=1 se queda
                    Config.XessFgOfrecer = true;
                    File.WriteAllText(Path.Combine(g, "framegen_type.txt"), "60=xess\r\n30=\r\n", new UTF8Encoding(false));
                    Ok(AjustesPc.PonerAlDia(g, false) == 0 && Leer(Path.Combine(g, "framegen_type.txt")).StartsWith("60=xess"), "7 con generacion_xess=1: XeSS FG se queda");
                    Config.XessFgOfrecer = false;
                    // los datos de verdad (copia): lo que haria con ellos
                    if (dataVer != null)
                    {
                        string gv = Path.Combine(work, @"quitados_usuario\games");
                        CopiarArbol(Path.Combine(fuenteData, "games"), gv, false);
                        string h = Huella(gv);
                        int cv = AjustesPc.PonerAlDia(gv, false);
                        Ok(cv > 0 || Huella(gv) == h, "7 con la copia de data\\ del usuario: " + cv + " cambios" + (cv == 0 ? " (no tiene XeSS FG ni FSR 3 FG guardados: nada tocado)" : ""));
                    }
                }

                // 8) versiones y pruebas
                {
                    string[][] vs = { new[] { "1.3", "1.3.1", "-1" }, new[] { "1.3.1", "1.3.1", "0" }, new[] { "1.3.10", "1.3.9", "1" }, new[] { "1.2.4-r7", "1.2.4", "0" },
                                      new[] { "1.4", "1.3.9", "1" }, new[] { "1.3", "1.3.0", "0" }, new[] { "v1.3.2", "1.3.1", "1" }, new[] { "2", "1.99.99", "1" } };
                    foreach (string[] v in vs)
                    {
                        int r = AjustesPc.CompararVersion(v[0], v[1]);
                        Ok(r.ToString(CultureInfo.InvariantCulture) == v[2], "8 version " + v[0] + " vs " + v[1] + ": " + r + " (esperado " + v[2] + ")");
                    }
                    Ok(AjustesPc.EsPrueba(new[] { "x.exe", "--test-avisos" }) && AjustesPc.EsPrueba(new[] { "--prueba-espera" }) && !AjustesPc.EsPrueba(new[] { "x.exe" }) &&
                       !AjustesPc.EsPrueba(new[] { "--ajustes-donde" }) && !AjustesPc.EsPrueba(new[] { "--inicio" }) && !AjustesPc.EsPrueba(new[] { "--ajustes" }),
                       "8 pruebas (--test-*, --prueba-*) a la raiz como siempre; la app, --inicio, --ajustes y --ajustes-donde a la carpeta del PC");
                    Ok(AjustesPc.EsAjuste("profiles.txt") && AjustesPc.EsAjuste("consoleenhancer.ini") && !AjustesPc.EsAjuste("test_avisos.txt") && !AjustesPc.EsAjuste("consoleenhancer.ini.antes-1.3") &&
                       !AjustesPc.EsAjuste("standby_plan.txt") && !AjustesPc.EsAjuste("consoleenhancer.log") && !AjustesPc.EsAjuste(".taskbar_hidden.txt") && !AjustesPc.EsAjuste("profiles.txt.tmp"),
                       "8 lo que se copia: ajustes si; informes de pruebas, copias, logs, temporales y estado de la espera no");
                }
            }
            catch (Exception ex) { fallos++; sb.AppendLine("FALLO excepcion: " + ex); }
            finally { Config.XessFgOfrecer = xeAntes; Config.FsrFgOfrecer = fsAntes; Config.GenEnVezDeDlss = envez; }
            return Fin(informe);
        }

        static int Fin(string informe)
        {
            sb.AppendLine(n + " pruebas, " + fallos + " fallos");
            string txt = sb.ToString();
            Console.Write(txt);
            try { File.WriteAllText(informe ?? Path.Combine(Paths.Datos, @"games\test_ajustes.txt"), txt, new UTF8Encoding(false)); } catch { }
            return fallos;
        }

        // --ajustes-donde[=<informe>]
        public static int Donde(string informe)
        {
            StringBuilder r = new StringBuilder();
            r.AppendLine("datos=" + Paths.Datos);
            r.AppendLine("modo=" + AjustesPc.Modo);
            r.AppendLine("pc=" + (AjustesPc.PcBase ?? "") + (AjustesPc.PcDePrueba ? " (CE_PC_DIR)" : ""));
            r.AppendLine("sello=" + AjustesPc.LeerSello(Paths.Datos));
            try
            {
                Config c = Config.Load();
                List<string> logs = new List<string>();
                Action<string> h = Log.Hook;
                Log.Hook = delegate(string m) { lock (logs) logs.Add(m); };
                try { AjustesPc.AlAbrir(c); } finally { Log.Hook = h; }
                foreach (string l in logs) { r.AppendLine("log: " + l); Log.Write(l); }
            }
            catch (Exception ex) { r.AppendLine("Config: " + ex.Message); }
            r.AppendLine("aviso=" + (AjustesPc.AvisoUnaVez() ?? ""));
            r.AppendLine("ini=" + File.Exists(Paths.IniFile) + " perfiles=" + File.Exists(Path.Combine(Paths.Datos, @"games\profiles.txt")));
            string t = r.ToString();
            Console.Write(t);
            try { if (!string.IsNullOrEmpty(informe)) File.WriteAllText(informe, t, new UTF8Encoding(false)); } catch { }
            return 0;
        }

        // ---------------------------------------------------------------- ayudas
        static bool Dentro(string a, string b)
        {
            if (string.IsNullOrEmpty(b)) return false;
            string x = Norm(a) + "\\", y = Norm(b) + "\\";
            return x.StartsWith(y, StringComparison.Ordinal);
        }
        static string Norm(string d) { try { return Path.GetFullPath(d).TrimEnd('\\').ToLowerInvariant(); } catch { return (d ?? "").ToLowerInvariant(); } }
        static string Leer(string f) { try { return File.Exists(f) ? File.ReadAllText(f).Trim() : "(no esta)"; } catch { return "(?)"; } }
        static string PrimeraLinea(string f) { try { return File.ReadAllLines(f)[0]; } catch { return "?"; } }

        static void CopiarArbol(string src, string dst, bool conSub)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
            {
                string t = Path.Combine(dst, Path.GetFileName(f));
                File.Copy(f, t, true);
                File.SetLastWriteTimeUtc(t, File.GetLastWriteTimeUtc(f));
            }
            if (!conSub) return;
            foreach (string d in Directory.GetDirectories(src))
            {
                string nm = Path.GetFileName(d);
                if (nm.Equals("debug", StringComparison.OrdinalIgnoreCase) || nm.Equals("diag_home", StringComparison.OrdinalIgnoreCase)) continue;   // (capturas: no hacen falta)
                if (nm.Equals("logs", StringComparison.OrdinalIgnoreCase)) { string c = Path.Combine(d, "cache"); if (Directory.Exists(c)) CopiarArbol(c, Path.Combine(Path.Combine(dst, "logs"), "cache"), false); continue; }
                if (nm.Equals("extras", StringComparison.OrdinalIgnoreCase) || nm.Equals("PrismaFg", StringComparison.OrdinalIgnoreCase)) continue;   // (DLL grandes: no hacen falta)
                CopiarArbol(d, Path.Combine(dst, nm), true);
            }
        }

        // Pone la fecha de todos los ficheros de ajustes de una carpeta de datos (y games\)
        static void Toca(string dir, DateTime t)
        {
            foreach (string sub in new[] { "", "games" })
            {
                string d = sub.Length == 0 ? dir : Path.Combine(dir, sub);
                if (!Directory.Exists(d)) continue;
                foreach (string f in Directory.GetFiles(d)) try { File.SetLastWriteTime(f, t); } catch { }
            }
        }

        // Lo de origen (ajustes de la carpeta y de games\) esta igual en destino
        static void Igual(string caso, string src, string dst)
        {
            if (dst == null) { Ok(false, caso + ": sin carpeta"); return; }
            int k = 0, mal = 0;
            string primero = null;
            foreach (string sub in new[] { "", "games" })
            {
                string s = sub.Length == 0 ? src : Path.Combine(src, sub);
                if (!Directory.Exists(s)) continue;
                foreach (string f in Directory.GetFiles(s))
                {
                    string nm = Path.GetFileName(f);
                    if (!AjustesPc.EsAjuste(nm)) continue;
                    k++;
                    string t = Path.Combine(sub.Length == 0 ? dst : Path.Combine(dst, "games"), Integrado.NombreIngles(nm));
                    if (!File.Exists(t) || Hash(t) != Hash(f)) { mal++; if (primero == null) primero = (sub.Length == 0 ? "" : "games\\") + nm; }
                }
            }
            Ok(k > 0 && mal == 0, caso + ": " + (k - mal) + " de " + k + " ficheros de ajustes iguales (de " + src + ")" + (primero != null ? "; distinto o falta: " + primero : ""));
        }

        static string Hash(string f)
        {
            using (MD5 m = MD5.Create()) using (FileStream s = File.OpenRead(f)) return BitConverter.ToString(m.ComputeHash(s));
        }
        // Todos los ficheros (con subcarpetas) y su contenido
        static string Huella(string dir)
        {
            StringBuilder h = new StringBuilder();
            List<string> fs = new List<string>(Directory.GetFiles(dir, "*", SearchOption.AllDirectories));
            fs.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in fs) h.Append(f.Substring(dir.Length)).Append('=').Append(Hash(f)).Append(';');
            return h.ToString();
        }

        // Unos datos de ejemplo (si no se dan copias de verdad): ini, perfiles, consola, lo elegido, un informe de pruebas y una copia del ini
        static void Ejemplo(string dir, string consola, DateTime t)
        {
            string g = Path.Combine(dir, "games");
            Directory.CreateDirectory(g);
            File.WriteAllText(Path.Combine(dir, "consoleenhancer.ini"), "# ejemplo " + consola + "\r\nidioma=es\r\n# ===== Plan de ConsoleEnhancer\r\ngeneracion_tipo=smooth\r\n# ce_ini_version=1.3\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "consoleenhancer.ini.antes-1.3"), "viejo", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "language.txt"), "es", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "scales_engine.txt"), "4k|30|d5=72\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(g, "console.txt"), consola, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(g, "profiles.txt"), "# perfiles\r\nPS5: Grand Theft Auto VI|3840x2160|3|1|1|1|30|1|1|,efg\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(g, "framegen_type.txt"), "60=efg\r\n30=\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(g, "test_avisos.txt"), "informe", new UTF8Encoding(false));
            Directory.CreateDirectory(Path.Combine(dir, @"logs\cache"));
            File.WriteAllText(Path.Combine(dir, @"logs\cache\ejemplo_0000.cso"), "cso", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, @"logs\consoleenhancer.log"), "log", new UTF8Encoding(false));
            Toca(dir, t);
        }
    }
}
