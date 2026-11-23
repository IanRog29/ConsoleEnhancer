// Switch2Prueba131.cs - 1.3.1 (agente SWITCH2): las partes 9-11 de --test-switch2 (src\Lanzador\Switch2Datos.cs), sin consola ni grafica:
//  9. Capturas reales del HOME: el nombre del juego seleccionado solo en su fila (encima de las casillas); la animacion de arranque de la
//     casilla de Ocarina (23-11 20:22:47) y el cursor moviendose (20:23:07) ya no dan un nombre (la 1.3: el logo de Arceus); el HOME quieto
//     si; el HOME atenuado nunca da otra seleccion.
// 10. Los ajustes de partida de un juego nuevo: los de su consola, no los de la de antes (PS5 -> Switch 2 y al reves), sobre una COPIA.
// 11. La migracion del "Juego 1" de las 20:22:52 sobre una COPIA de los datos del usuario.
// C# 5
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        const string OcarinaPrueba = "The Legend of Zelda: Ocarina of Time";

        static void Switch2Prueba131(StringBuilder sb, Action<string, bool> check, string tmp, string datosUsuario, string viejo)
        {
            // ---- 9. Capturas: la fila de los nombres ----
            string gu = datosUsuario != null ? Path.Combine(datosUsuario, "games") : null;
            string anim = gu != null ? Path.Combine(gu, @"diag_home\20261123-202247-562_sin_nombre.png") : null;
            string mueve = gu != null ? Path.Combine(gu, @"diag_home\20261123-202307-806_sin_nombre.png") : null;
            string quieto = gu != null ? Path.Combine(gu, @"debug\ps5\20261123-202244_1280x720.png") : null;
            if (anim != null && File.Exists(anim))
            {
                check("9. 20:22:47, la animacion de arranque de la casilla de Ocarina: ningun nombre (la 1.3 tomo el logo de \"Leyendas Pokémon: Arceus\" por otro juego)",
                      NombreEnCaptura(anim) == null);
                if (File.Exists(mueve))
                    check("9. 20:23:07, el cursor moviendose (el nombre aun sin salir): ningun nombre (la 1.3: el mismo logo, reconocido como \"Juego 1\")", NombreEnCaptura(mueve) == null);
            }
            else sb.Append("      9. (sin la captura de la animacion de las 20:22:47 en estos datos)\r\n");
            if (quieto != null && File.Exists(quieto))
            {
                HomeLabel.LabelText lt;
                Rectangle box;
                byte[] lab = NombreEnCaptura(quieto, out lt, out box);
                check("9. 20:22:44, el HOME quieto con Ocarina seleccionado: su nombre, en su fila (centro y " + (lt != null ? ((lt.Y0 + lt.Y1) / 2).ToString(CultureInfo.InvariantCulture) : "-") + " de 360)",
                      lab != null && HomeLabel.EnFilaDeNombres(lt));
                check("9. ...y sigue siendo el HOME de la Switch 2 entero (nombre cian + hora con la bateria: prueba concluyente con la PS5 puesta)", ReconoceHome(quieto));
                // El HOME atenuado (un aviso encima): nunca otra seleccion: o no hay nombre, o DimHome lo ve atenuado (AnalyzeGrab lo quita), o
                // apenas oscurecido el icono de debajo sigue siendo el mismo juego
                bool dimOk = true;
                StringBuilder dimTxt = new StringBuilder();
                using (Bitmap full = Switch2Datos.Cargar32(quieto))
                using (Bitmap small = HomeLabel.Small(full))
                {
                    double[] rf = HomeLabel.Strips(small);
                    Rectangle b0; bool h0; HomeLabel.LabelText l0; int hy0;
                    HomeLabel.Selected(small, out b0, out h0, out l0, out hy0);
                    double[] ic0 = HomeLabel.Icon(small, b0.X + b0.Width / 2);
                    foreach (double k in new[] { 0.9, 0.8, 0.7, 0.5, 0.3 })
                        using (Bitmap dim = Oscurece(small, k))
                        {
                            Rectangle b; bool h; HomeLabel.LabelText l; int hy;
                            byte[] m = HomeLabel.Selected(dim, out b, out h, out l, out hy);
                            double dk, dc;
                            bool atenuado = HomeLabel.DimHome(HomeLabel.Strips(dim), rf, out dk, out dc);
                            double di = m != null ? HomeLabel.Diff(HomeLabel.Icon(dim, b.X + b.Width / 2), ic0) : -1;
                            dimTxt.Append(" x").Append(k.ToString("0.0", CultureInfo.InvariantCulture)).Append(": ").Append(m != null ? "nombre" : "sin nombre")
                                  .Append(atenuado ? ", atenuado" : "").Append(di >= 0 ? ", icono a " + di.ToString("0", CultureInfo.InvariantCulture) : "");
                            if (m != null && !atenuado && di > HomeLabel.SameIcon) dimOk = false;
                        }
                }
                check("9. El HOME atenuado (oscurecido como con un aviso encima):" + dimTxt + ": nunca otra seleccion", dimOk);
            }
            // Todas las de diag_home (las del usuario y las de la app de antes) que no son la biblioteca: ninguna da un nombre (eran textos cian de
            // las casillas, de avisos o de otras pantallas: el OCR no leia nada) y las del HOME de la app de antes con nombre, si
            int diagN = 0, diagMal = 0;
            List<string> diag = new List<string>();
            foreach (string dir in new[] { gu != null ? Path.Combine(gu, "diag_home") : null, viejo != null ? Path.Combine(viejo, "diag_home") : null })
                if (dir != null && Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*_sin_nombre.png")) diag.Add(f);
            foreach (string f in diag)
            {
                // (las del 16-11 a las 11:00 son de la fuente de prueba de barras de color de la app de antes, no de una consola)
                if (Path.GetFileName(f).StartsWith("20261116-1100", StringComparison.Ordinal)) continue;
                using (Bitmap full = Switch2Datos.Cargar32(f))
                using (Bitmap small = HomeLabel.Small(full))
                {
                    if (HomeLabel.IsLibrary(small)) continue;
                    Rectangle b; bool h; HomeLabel.LabelText l; int hy;
                    diagN++;
                    if (HomeLabel.Selected(small, out b, out h, out l, out hy) != null) { diagMal++; sb.Append("      9. con nombre: ").Append(f).Append("\r\n"); }
                }
            }
            if (diagN > 0) check("9. Las capturas \"sin nombre\" de diag_home (" + diagN + ": el OCR no leia nada; logos de casillas, avisos, pantallas de mandos): ninguna da un nombre ahora (" + diagMal + ")", diagMal == 0);
            int homeN = 0, homeOk = 0;
            if (viejo != null && Directory.Exists(Path.Combine(viejo, "test_biblioteca")))
                foreach (string f in Directory.GetFiles(Path.Combine(viejo, "test_biblioteca"), "home_0*.png"))
                {
                    bool conNombre = Path.GetFileName(f).IndexOf("apagado", StringComparison.Ordinal) < 0;
                    HomeLabel.LabelText l; Rectangle b;
                    bool hay = NombreEnCaptura(f, out l, out b) != null;
                    homeN++;
                    if (hay == conNombre) homeOk++;
                }
            if (homeN > 0) check("9. Las del HOME de la app de antes (test_biblioteca\\home_*): el nombre en las que lo tienen, ninguno con el cursor en el boton de apagado (" + homeOk + " de " + homeN + ")", homeOk == homeN);

            // ---- 10. Los ajustes de partida de un juego nuevo: los de SU consola (copia de los perfiles del usuario) ----
            if (gu != null && File.Exists(Path.Combine(gu, "profiles.txt")))
            {
                string g10 = Path.Combine(tmp, "games10");
                Directory.CreateDirectory(g10);
                foreach (string n in new[] { "profiles.txt", "game_icons.txt", "last_game.txt" })
                    if (File.Exists(Path.Combine(gu, n))) File.Copy(Path.Combine(gu, n), Path.Combine(g10, n), true);
                string antesP = GameProfiles.CarpetaDePrueba(g10);
                string consolaAntes = Consolas.Elegida;
                try
                {
                    GameProfile cons = GameProfiles.Find(ConsolaPerfil.NombrePerfilConsola);
                    GameProfile oot = GameProfiles.Find(OcarinaPrueba);
                    string ultimo = LeerTxt(Path.Combine(g10, "last_game.txt"));
                    string sem = SiembraUltimos(g10);
                    check("10. Al abrir (sin games\\console_last_game.txt): el ultimo juego (\"" + ultimo + "\") es el de su consola: " + (sem ?? "nada"),
                          ultimo == null || sem != null || !GameProfiles.EsDeSwitch2(ultimo));
                    if (cons != null && oot != null && sem == Consolas.Switch2.Clave + "|" + OcarinaPrueba)
                    {
                        TrayApp t = new TrayApp(new Config(), true);
                        // Como el usuario a las 20:22:38: la app abierta con la PS5 y sus ajustes de toda la consola; la Switch 2 confirmada
                        t.PonAjustesPrueba(cons);
                        t.AjustesSonDe(Consolas.Ps5);
                        Consolas.Reiniciar("switch2");
                        string de;
                        GameProfile np = t.PerfilDePartida("Juego 9", out de);
                        check("10. PS5 -> Switch 2: un juego nuevo empieza con " + de + " (" + np.Summary() + "), no con los de la PS5 (" + cons.Summary() + ")",
                              de != null && MismosAjustes(np, oot));
                        double[] icNuevo = new double[180];
                        for (int i = 0; i < 180; i++) icNuevo[i] = (i * 37) % 251;
                        GameProfile jn = t.EntryProfile("", icNuevo);
                        GameProfile guardado = jn != null ? GameProfiles.Find(jn.Name) : null;
                        check("10. ...y al entrar en uno sin nombre leido: \"" + (jn != null ? jn.Name : "-") + "\" guardado con esos (" + (guardado != null ? guardado.Summary() : "-") + ")",
                              jn != null && GameNames.IsGeneric(jn.Name) && guardado != null && MismosAjustes(guardado, oot) && !MismosAjustes(guardado, cons));
                        // Con los ajustes de ahora de la misma consola: como siempre, los de ahora
                        t.PonAjustesPrueba(cons);
                        t.AjustesSonDe(Consolas.Switch2);
                        np = t.PerfilDePartida("Juego 10", out de);
                        check("10. Switch 2 con los ajustes de ahora de la Switch 2: un juego nuevo con los de ahora, como siempre (" + np.Summary() + ")",
                              de == null && MismosAjustes(np, cons));
                        // Sin ultimo juego guardado de la Switch 2: los de serie
                        File.Delete(Path.Combine(g10, "console_last_game.txt"));
                        t.AjustesSonDe(Consolas.Ps5);
                        np = t.PerfilDePartida("Juego 11", out de);
                        Config dc = new Config();
                        check("10. PS5 -> Switch 2 sin ningun juego suyo guardado: " + de + " (" + np.Summary() + ")",
                              de != null && de.IndexOf("de serie", StringComparison.Ordinal) >= 0 && np.Level == dc.Dlss5DefaultLevel && np.Passes == 1 &&
                              !np.Cap && !np.Mode120 && np.Dlss5 && np.Fg && np.Pick60 == "" && np.Pick30 == "" && Consolas.Switch2.CapturaUtil(np.Capture));
                        // Al reves: Switch 2 -> PS5: su perfil "Consola", no los de Ocarina
                        t.PonAjustesPrueba(oot);
                        t.AjustesSonDe(Consolas.Switch2);
                        Consolas.Reiniciar("ps5");
                        np = t.PerfilDePartida("PS5: Juego de prueba", out de);
                        check("10. Switch 2 -> PS5: un juego nuevo de la PS5 empieza con " + de + " (" + np.Summary() + "), no con los de Ocarina (" + oot.Summary() + ")",
                              de != null && MismosAjustes(np, cons) && !MismosAjustes(np, oot));
                        // Al reves sin "Consola" (la primera vez con la PS5): los de serie, no los de la Switch 2
                        string sinCons = Path.Combine(tmp, "games10b");
                        Directory.CreateDirectory(sinCons);
                        File.WriteAllText(Path.Combine(sinCons, "profiles.txt"), "# prueba\r\n" + OcarinaPrueba + "|1920x1080|2|2|1|1|60|1|1|efg,\r\n", new UTF8Encoding(false));
                        GameProfiles.CarpetaDePrueba(sinCons);
                        np = t.PerfilDePartida(ConsolaPerfil.NombrePerfilConsola, out de);
                        check("10. Switch 2 -> PS5 sin su perfil \"Consola\" aun: " + de + " (" + np.Summary() + "), no los de Ocarina",
                              de != null && de.IndexOf("de serie", StringComparison.Ordinal) >= 0 && np.Pick60 == "" && np.Passes == 1);
                        GameProfiles.CarpetaDePrueba(g10);
                        // Lecturas malas del nombre en el menu de la PS5 (su log de la 1.3): ni perfiles nuevos ni otro juego
                        int antesN = GameProfiles.Nombres().Count;
                        StringBuilder bas = new StringBuilder();
                        bool basOk = true;
                        foreach (string[] caso in new[] { new[] { "mn noo", "Consola" }, new[] { "T4LANT_LBL *", "Consola" }, new[] { "Theft Auto VI", "PS5: Grand Theft Auto VI" } })
                        {
                            t.enterSure = true; t.enterConfirmed = true;   // (como al salir de su menu: MenuPs5Salir)
                            GameProfile gp = t.EntryProfile("PS5: " + caso[0], null);
                            bas.Append(" \"").Append(caso[0]).Append("\" -> ").Append(gp != null ? "\"" + gp.Name + "\"" : "-").Append(";");
                            if (gp == null || gp.Name != caso[1]) basOk = false;
                        }
                        check("10. Lecturas malas del menu de la PS5 (20:12:27, 20:22:03, 20:22:16) al salir con ellas:" + bas + " ningun perfil nuevo (" +
                              GameProfiles.Nombres().Count + " de " + antesN + ")", basOk && GameProfiles.Nombres().Count == antesN && GameProfiles.Find("PS5: mn noo") == null);
                        check("10. ...y los nombres de verdad cortos o en minusculas siguen valiendo (\"Ico\", \"nba 2k\", \"Bloodborne\")",
                              !PareceBasuraOcr("Ico") && !PareceBasuraOcr("nba 2k") && !PareceBasuraOcr("Bloodborne") && PareceBasuraOcr("mn noo"));
                        // Al entrar en un juego con la consola confirmada: su ultimo juego queda apuntado (para la proxima vez)
                        t.JuegoPuestoDeConsola(GameProfiles.Find("PS5: Grand Theft Auto VI"));
                        check("10. Al entrar en \"PS5: Grand Theft Auto VI\" con la PS5: su ultimo juego (games\\console_last_game.txt) y los ajustes de ahora, de la PS5",
                              UltimoJuegoDe(Consolas.Ps5.Clave) == "PS5: Grand Theft Auto VI" && t.ajustesDe == Consolas.Ps5.Clave);
                    }
                    else sb.Append("      10. (sin \"Consola\" u Ocarina en los perfiles de estos datos, o el ultimo juego no es Ocarina: solo la siembra)\r\n");
                }
                finally { GameProfiles.CarpetaDePrueba(antesP); Consolas.Reiniciar(consolaAntes); }
            }

            // ---- 11. La migracion del "Juego 1" de las 20:22:52 (copia de los datos del usuario de la 1.3) ----
            string logU = datosUsuario != null ? Path.Combine(datosUsuario, @"logs\consoleenhancer.log") : null;
            bool hayCaso = false;
            if (logU != null && File.Exists(logU))
                using (FileStream fs = new FileStream(logU, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string l;
                    while ((l = sr.ReadLine()) != null) if (l.StartsWith("2026-11-23 20:22:52.326  Juego nuevo: \"Juego 1\"", StringComparison.Ordinal)) { hayCaso = true; break; }
                }
            if (!hayCaso) { sb.Append("      11. (estos datos no son los de la 1.3 del 23-11: sin el \"Juego 1\" de las 20:22:52)\r\n"); return; }
            string g11 = Path.Combine(tmp, "games11");
            Directory.CreateDirectory(g11);
            foreach (string n in new[] { "profiles.txt", "game_icons.txt", "last_game.txt", Switch2Datos.MarcaImportado, Switch2Datos.MarcaNombres })
                if (File.Exists(Path.Combine(gu, n))) File.Copy(Path.Combine(gu, n), Path.Combine(g11, n), true);
            string logCopia = Path.Combine(tmp, "consoleenhancer11.log");
            using (FileStream fs = new FileStream(logU, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (FileStream fo = new FileStream(logCopia, FileMode.Create)) fs.CopyTo(fo);
            string antes11 = GameProfiles.CarpetaDePrueba(g11);
            try
            {
                GameProfile j1 = GameProfiles.Find("Juego 1");
                double[] icJ1 = j1 != null ? j1.Icon : null;
                GameProfile oot0 = GameProfiles.Find(OcarinaPrueba);
                string ootAntes = oot0 != null ? oot0.Summary() : null;
                int n0 = GameProfiles.Nombres().Count;
                check("11. En la copia de sus datos de la 1.3 esta \"Juego 1\" (" + (j1 != null ? j1.Summary() : "-") + ": los de la PS5)", j1 != null && !j1.NameSure && icJ1 != null);
                // El icono de "Juego 1" casaba con la captura del cursor moviendose (20:23:07: "HOME: seleccionado "Juego 1" (reconocido por su icono)")
                List<KeyValuePair<string, string>> r = Switch2Datos.AlAbrir(g11, null, viejo, new[] { logCopia }, new DateTime(2026, 11, 23, 23, 0, 0));
                bool hecho = r.Exists(delegate (KeyValuePair<string, string> kv) { return kv.Key == "Juego 1" && kv.Value == OcarinaPrueba; });
                GameProfile oot1 = GameProfiles.Find(OcarinaPrueba);
                check("11. \"Juego 1\" (Ocarina arrancado a las 20:22:52) quitado con sus iconos; el juego de verdad, Ocarina",
                      hecho && GameProfiles.Find("Juego 1") == null && (icJ1 == null || GameProfiles.FindByIcon(icJ1) == null));
                check("11. Los demas siguen (" + GameProfiles.Nombres().Count + " de " + n0 + "): Ocarina con sus ajustes (" + (oot1 != null ? oot1.Summary() : "-") + "), \"Consola\", los de la PS5",
                      GameProfiles.Nombres().Count == n0 - 1 && oot1 != null && oot1.Summary() == ootAntes && GameProfiles.Find("Consola") != null &&
                      GameProfiles.Find("PS5: Grand Theft Auto VI") != null);
                // El HOME quieto de las 20:22:44: Ocarina por su icono (lo que vio la app), y ya ningun "Juego N"
                if (quieto != null && File.Exists(quieto))
                {
                    HomeLabel.LabelText lt; Rectangle box;
                    double[] ic = null;
                    using (Bitmap full = Switch2Datos.Cargar32(quieto))
                    using (Bitmap small = HomeLabel.Small(full))
                    {
                        bool h; int hy;
                        if (HomeLabel.Selected(small, out box, out h, out lt, out hy) != null) ic = HomeLabel.Icon(small, box.X + box.Width / 2);
                    }
                    GameProfile k = ic != null ? GameProfiles.FindByIcon(ic) : null;
                    check("11. En el HOME de las 20:22:44 la casilla seleccionada es \"" + (k != null ? k.Name : "-") + "\" por su icono", k != null && k.Name == OcarinaPrueba);
                }
                List<KeyValuePair<string, string>> r2 = Switch2Datos.AlAbrir(g11, null, viejo, new[] { logCopia }, new DateTime(2026, 11, 23, 23, 5, 0));
                check("11. Al abrir otra vez: nada mas (una vez, games\\name_fixes.txt)", r2.Count == 0 && GameProfiles.Nombres().Count == n0 - 1);
            }
            finally { GameProfiles.CarpetaDePrueba(antes11); }
        }

        static byte[] NombreEnCaptura(string f)
        {
            HomeLabel.LabelText l; Rectangle b;
            return NombreEnCaptura(f, out l, out b);
        }

        static byte[] NombreEnCaptura(string f, out HomeLabel.LabelText label, out Rectangle box)
        {
            using (Bitmap full = Switch2Datos.Cargar32(f))
            using (Bitmap small = HomeLabel.Small(full))
            {
                bool h; int hy;
                return HomeLabel.Selected(small, out box, out h, out label, out hy);
            }
        }

        // Una copia de la imagen con el brillo por k (como el HOME atenuado por un aviso encima)
        static Bitmap Oscurece(Bitmap src, double k)
        {
            Bitmap b = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix(new float[][] {
                new float[] { (float)k, 0, 0, 0, 0 }, new float[] { 0, (float)k, 0, 0, 0 }, new float[] { 0, 0, (float)k, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 } });
            using (System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes())
            using (Graphics g = Graphics.FromImage(b))
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return b;
        }
    }
}
