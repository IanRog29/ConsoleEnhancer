// PruebaDeteccion.cs - Prueba sin consola de src\DetectorMenu.cs (19-11, agente DETECCION): capturas de verdad con su etiqueta, antes y
// ahora, y escenas (juego -> menu -> juego, la pantalla de un juego de la PS5, la pantalla atenuada, el negro al despertar o al arrancar un
// juego...) con el ritmo de capturas de la app y un reloj simulado: aciertos, falsos positivos y cuanto se tarda en decidir.
//   Switch2Magpie.exe --test-deteccion=<carpeta de imagenes>;<informe>     (games\test_deteccion; docs\CONSOLAS.md, seccion 9)
//   Switch2Magpie.exe --test-deteccion=listar;<carpeta>;<informe.tsv>      (cada imagen: lo de antes y lo de ahora, para etiquetar)
// Las imagenes: <etiqueta>_<lo que sea>_HHmmss.(png|jpg); la hora del nombre es la "del PC" de esa imagen. Etiquetas: menu (con la hora),
// menutexto (pantalla de la consola sin la hora), oscuro (el menu atenuado), negro, juego, pc (otra cosa: no es la consola), sw2home (el
// HOME de la Switch 2), sw2juego, xboxmenu / xboxotra (dibujadas), ps5dudoso (20-11: pantallas de la PS5 cuya hora no lee su lector,
// hechas de las de verdad: el avatar junto a la hora, algo a su derecha, la hora en otro sitio, un fundido). Seccion 4 (20-11, agente
// CONSOLA): consola=auto, que consola es (la PS5 no se puede tomar por la Xbox). C# 5
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ConsoleEnhancer
{
    static class PruebaDeteccion
    {
        sealed class Img
        {
            public string File, Etiqueta;
            public DateTime Now;
            public Bitmap Ligera, Gris;
        }

        // Lo que sale de una imagen con el lector de antes y el de ahora (en el menu y jugando)
        sealed class Res
        {
            public bool AntesReloj, AntesFrase;
            public double AntesMs;
            public LecturaMenu AhoraJugando, AhoraMenu;
        }

        public static int Run(string arg)
        {
            string[] p = arg.Split(';');
            if (p[0] == "listar")
                return Listar(p[1], p.Length > 2 ? p[2] : Path.Combine(p[1], "listado.tsv"), p.Length > 3 ? (Consolas.Buscar(p[3]) ?? Consolas.Ps5) : Consolas.Ps5);
            string dir = p[0], rep = p.Length > 1 ? p[1] : Path.Combine(dir, "prueba_deteccion.txt");
            // 21-11 (1.2.1): --test-deteccion=<carpeta>;<informe>;<carpeta de escenas 1.2.1> (seccion 5: arranque, apagado; sin las imagenes
            // del banco, solo la seccion 5: --test-deteccion=-;<informe>;<carpeta de escenas> con las del banco en la de escenas)
            string extra = p.Length > 2 && p[2].Length > 0 ? p[2] : null;
            if (dir == "-") return SoloEscenas121(rep, extra);
            return Informe(dir, rep, extra);
        }

        static List<Img> Cargar(string dir, bool gris)
        {
            List<string> files = new List<string>();
            foreach (string ext in new[] { "*.jpg", "*.png" }) files.AddRange(Directory.GetFiles(dir, ext));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            List<Img> r = new List<Img>();
            foreach (string f in files)
            {
                string bn = Path.GetFileNameWithoutExtension(f);
                if (bn.StartsWith("_")) continue;
                Img im = new Img();
                im.File = f;
                int us = bn.IndexOf('_');
                im.Etiqueta = us > 0 ? bn.Substring(0, us) : bn;
                im.Now = Hora(f);
                using (Bitmap src = new Bitmap(f)) im.Ligera = Ligera(src);
                if (gris) im.Gris = Gris(im.Ligera);
                r.Add(im);
            }
            return r;
        }

        static DateTime Hora(string f)
        {
            DateTime now = File.GetLastWriteTime(f);
            string bn = Path.GetFileNameWithoutExtension(f);
            Match mt = Regex.Match(bn, @"_(\d{2})(\d{2})(\d{2})$");
            if (!mt.Success) mt = Regex.Match(bn, @"(?:^|_)\d{8}-(\d{2})(\d{2})(\d{2})");
            if (mt.Success)
                now = new DateTime(2026, 11, 19).AddHours(int.Parse(mt.Groups[1].Value)).AddMinutes(int.Parse(mt.Groups[2].Value)).AddSeconds(int.Parse(mt.Groups[3].Value));
            return now;
        }

        static Res Clasificar(Bitmap b, DateTime now, ConsolaPerfil c)
        {
            Res r = new Res();
            Stopwatch sw = Stopwatch.StartNew();
            string porque, hora;
            r.AntesReloj = RelojPs5.PuedeSer(b, out porque) && RelojPs5.EsHora(RelojPs5.Leer(b), now, out hora);
            string frase;
            if (!r.AntesReloj) r.AntesFrase = RelojPs5.EsPantallaSistema(HomeLabel.ScreenText(b), out frase);
            r.AntesMs = sw.Elapsed.TotalMilliseconds;
            DetectorMenu.OlvidaZonas();
            r.AhoraJugando = DetectorMenu.Clasifica(b, now, c, false, false);
            DetectorMenu.OlvidaZonas();
            r.AhoraMenu = DetectorMenu.Clasifica(b, now, c, true, false);
            return r;
        }

        static int Listar(string dir, string rep, ConsolaPerfil c)
        {
            StringBuilder sb = new StringBuilder("imagen\thora\tantes_reloj\tantes_frase\tahora_jugando\tahora_menu\tfrase\tbrillo\tms\tocr\tporque\tpantalla\r\n");
            foreach (Img im in Cargar(dir, false))
            {
                Res r = Clasificar(im.Ligera, im.Now, c);
                sb.Append(Path.GetFileName(im.File)).Append('\t').Append(im.Now.ToString("HH:mm:ss")).Append('\t').Append(r.AntesReloj ? 1 : 0).Append('\t')
                  .Append(r.AntesFrase ? 1 : 0).Append('\t').Append(r.AhoraJugando.Vista).Append(r.AhoraJugando.Fuerte ? "+" : "").Append('\t')
                  .Append(r.AhoraMenu.Vista).Append('\t').Append(r.AhoraMenu.Frase).Append('\t').Append(r.AhoraMenu.Brillo99).Append('\t')
                  .Append(r.AhoraMenu.Ms.ToString("0")).Append('\t').Append(Plano(r.AhoraJugando.Ocr)).Append('\t').Append(r.AhoraMenu.Porque)
                  .Append('\t').Append(Plano(r.AhoraMenu.Pantalla)).Append("\r\n");
                im.Ligera.Dispose();
            }
            File.WriteAllText(rep, sb.ToString(), new UTF8Encoding(false));
            return 0;
        }

        static string Plano(string s) { return s == null ? "" : s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " "); }

        // ---------------- El informe ----------------
        static bool EsMenu(string et) { return et == "menu" || et == "menutexto" || et == "oscuro"; }
        static bool EsJuego(string et) { return et == "juego" || et == "pc"; }

        static int Informe(string dir, string rep) { return Informe(dir, rep, null); }

        static int Informe(string dir, string rep, string extra)
        {
            StringBuilder sb = new StringBuilder();
            List<Img> imgs = Cargar(dir, true);
            Dictionary<string, Res> res = new Dictionary<string, Res>();
            int fallos = 0;
            sb.Append("== 1. Cada imagen (PS5), captura ligera 1280x720 y la gris del motor (la Y en rango limitado) ==\r\n");
            sb.Append("Antes: la hora (RelojPs5) o, en el menu, una frase de su lista de antes. Ahora: DetectorMenu.Clasifica.\r\n");
            sb.Append("'entra' = jugando, se veria el menu; 'se queda' = en el menu, no cuenta para salir (Menu/MenuTexto/Oscura/Plana).\r\n\r\n");
            // Por etiqueta: aciertos de antes y de ahora
            Dictionary<string, int[]> cuenta = new Dictionary<string, int[]>(); // n, antesEntra, ahoraEntra, antesQueda, ahoraQueda, ahoraFuerte
            double msAntes = 0, msAhora = 0; int nMs = 0;
            StringBuilder det = new StringBuilder();
            foreach (Img im in imgs)
            {
                foreach (string var in new[] { "ligera", "gris" })
                {
                    Bitmap b = var == "gris" ? im.Gris : im.Ligera;
                    Res r = Clasificar(b, im.Now, Consolas.Ps5);
                    res[im.File + "|" + var] = r;
                    bool antesEntra = r.AntesReloj, ahoraEntra = r.AhoraJugando.Vista == VistaMenu.Menu;
                    bool antesQueda = r.AntesReloj || r.AntesFrase;
                    VistaMenu vm = r.AhoraMenu.Vista;
                    bool ahoraQueda = vm != VistaMenu.Juego;
                    string k = im.Etiqueta + " (" + var + ")";
                    int[] c;
                    if (!cuenta.TryGetValue(k, out c)) cuenta[k] = c = new int[6];
                    c[0]++;
                    if (antesEntra) c[1]++;
                    if (ahoraEntra) c[2]++;
                    if (antesQueda) c[3]++;
                    if (ahoraQueda) c[4]++;
                    if (r.AhoraJugando.Fuerte) c[5]++;
                    msAntes += r.AntesMs; msAhora += r.AhoraMenu.Ms; nMs++;
                    // Fallos de ahora: un juego que entra en el menu, o un menu (con la hora) que no
                    bool mal = (EsJuego(im.Etiqueta) && ahoraEntra) || (im.Etiqueta == "menu" && !ahoraEntra && vm == VistaMenu.Juego) ||
                               (im.Etiqueta == "menutexto" && !ahoraQueda) || (im.Etiqueta == "oscuro" && !ahoraQueda) || (im.Etiqueta == "negro" && vm != VistaMenu.Plana);
                    if (mal) fallos++;
                    if (mal || antesEntra != ahoraEntra || antesQueda != ahoraQueda)
                        det.Append(mal ? "FALLO " : "      ").Append(Path.GetFileName(im.File)).Append(" (").Append(var).Append("): antes ")
                           .Append(antesEntra ? "entra" : "no entra").Append(antesQueda ? "/se queda" : "/sale").Append("; ahora ")
                           .Append(r.AhoraJugando.Vista).Append(r.AhoraJugando.Fuerte ? " (fuerte)" : "").Append(" / ").Append(vm)
                           .Append(r.AhoraMenu.Frase != null ? " \"" + r.AhoraMenu.Frase + "\"" : "").Append(r.AhoraMenu.Porque != null ? " (" + r.AhoraMenu.Porque + ")" : "").Append("\r\n");
                }
            }
            sb.Append("etiqueta (captura)          n | entra: antes ahora (fuerte) | se queda en el menu: antes ahora\r\n");
            List<string> keys = new List<string>(cuenta.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys)
            {
                int[] c = cuenta[k];
                sb.Append(k.PadRight(26)).Append(c[0].ToString().PadLeft(4)).Append(" | ").Append(Pc(c[1], c[0])).Append(' ').Append(Pc(c[2], c[0]))
                  .Append(" (").Append(c[5]).Append(")").Append(" | ").Append(Pc(c[3], c[0])).Append(' ').Append(Pc(c[4], c[0])).Append("\r\n");
            }
            sb.Append("\r\nTiempo medio por captura: antes ").Append((msAntes / Math.Max(1, nMs)).ToString("0")).Append(" ms (con OCR de la pantalla si no habia hora), ahora ")
              .Append((msAhora / Math.Max(1, nMs)).ToString("0")).Append(" ms (en el menu)\r\n\r\n");
            sb.Append("Diferencias y fallos:\r\n").Append(det).Append("\r\n");

            // ---------------- 2. Escenas ----------------
            sb.Append("== 2. Escenas con el ritmo de la app (reloj simulado; la lectura de cada captura con su tiempo medido) ==\r\n");
            sb.Append("Antes: capturas cada 0,5 s en el menu, 1,5 s jugando (4K), 0,4 s confirmando; 2 con la hora para entrar, 3 sin ella para salir; la\r\n");
            sb.Append("decision en el siguiente HomeTick (cada 0,5 s). Ahora: 0,25 s confirmando, 1 captura si la hora es la del minuto, 2 para salir,\r\n");
            sb.Append("negro y oscuro no cuentan, la decision tras la captura. El cambio en si (telon, Magpie, motor) aparte: ver el informe.\r\n\r\n");
            Dictionary<string, List<Img>> por = new Dictionary<string, List<Img>>();
            foreach (Img im in imgs)
            {
                List<Img> l;
                if (!por.TryGetValue(im.Etiqueta, out l)) por[im.Etiqueta] = l = new List<Img>();
                l.Add(im);
            }
            List<Img> menu = Get(por, "menu"), juego = Get(por, "juego"), texto = Get(por, "menutexto"), oscuro = Get(por, "oscuro"), negro = Get(por, "negro"), pc = Get(por, "pc");
            int escFallos = 0;
            foreach (string var in new[] { "ligera", "gris" })
            {
                sb.Append("-- captura ").Append(var).Append(" --\r\n");
                escFallos += Escena(sb, res, var, "Jugando, el menu 12 s y de vuelta al juego", false,
                                    new Tramo(juego, 20), new Tramo(menu, 12), new Tramo(juego, 12));
                escFallos += Escena(sb, res, var, "En el menu, la pantalla de un juego sin la hora 20 s y vuelta", true,
                                    new Tramo(menu, 6), new Tramo(texto, 20), new Tramo(menu, 6));
                escFallos += Escena(sb, res, var, "En el menu, atenuado 60 s (no tocar el mando)", true,
                                    new Tramo(menu, 6), new Tramo(oscuro, 60));
                escFallos += Escena(sb, res, var, "Al abrir la app: negro 6 s (despertando) y el menu", true,
                                    new Tramo(negro, 6, true), new Tramo(menu, 10));
                escFallos += Escena(sb, res, var, "En el menu, se arranca un juego: negro 4 s y el juego", true,
                                    new Tramo(menu, 6), new Tramo(negro, 4), new Tramo(juego, 12));
                escFallos += Escena(sb, res, var, "Jugando 120 s (falsas entradas al menu)", false, new Tramo(juego, 120));
                if (pc.Count > 0) escFallos += Escena(sb, res, var, "Otra cosa por HDMI (el PC) 60 s", false, new Tramo(pc, 60));
                sb.Append("\r\n");
            }
            int otras = OtrasConsolas(sb, imgs);
            int auto = Auto(sb, imgs); // (20-11, CONSOLA)
            int e121 = 0;
            if (extra != null) e121 = Escenas121(sb, imgs, Cargar(extra, true)); // (21-11, 1.2.1)
            sb.Insert(0, "Prueba de la deteccion del menu (src\\DetectorMenu.cs): " + fallos + " fallos por imagen (PS5), " + escFallos + " escenas mal (ahora), " +
                         otras + " fallos de Xbox / Switch 2 / consola=auto, " + auto + " escenas de consola=auto mal (seccion 4)" +
                         (extra != null ? ", " + e121 + " escenas 1.2.1 mal (seccion 5)" : "") + "; " + imgs.Count + " imagenes de " + dir + "\r\n\r\n");
            fallos += otras + auto + e121;
            File.WriteAllText(rep, sb.ToString(), new UTF8Encoding(false));
            return fallos + escFallos;
        }

        // Solo la seccion 5 (rapida: sin el OCR de todo el banco). Las imagenes del menu y de un juego oscuro, de la misma carpeta de escenas
        // (menu_*, juego_gta_* ...) o del banco si se le copian
        static int SoloEscenas121(string rep, string extra)
        {
            StringBuilder sb = new StringBuilder();
            List<Img> ex = extra != null ? Cargar(extra, true) : new List<Img>();
            int mal = Escenas121(sb, ex, ex);
            sb.Insert(0, "Prueba 1.2.1 (seccion 5): " + mal + " escenas mal; " + ex.Count + " imagenes de " + extra + "\r\n\r\n");
            File.WriteAllText(rep, sb.ToString(), new UTF8Encoding(false));
            return mal;
        }

        // ---------------- 5. (21-11, 1.2.1) Arranque y apagado de la consola: ningun perfil de juego ----------------
        // El log del usuario del 21-11: 19:09:20 la app abierta desde la espera al encender la PS5 salio del menu con las particulas de la
        // Bienvenida (perfil "Consola" con Magpie, 7,8 s de telon negro); 19:08:48, tras el menu de apagado, la pantalla de apagarse saco al
        // perfil de GTA VI. Escenas con lo que hace la app (MenuPs5Captura): Clasifica con el texto de la pantalla en el menu cada 1 s, el menu
        // de apagado (RelojPs5.EsMenuApagado) pone la salida prudente 20 s y su negro cuenta como atenuado; MaquinaMenu decide. Etiquetas de la
        // carpeta de escenas: arranque (logo, Bienvenida), apagado (pantalla de apagarse), menuapagado (el inicio con el menu de apagado),
        // juego, juegooscuro; del banco: menu y juego (GTA VI de noche: juego_gta_145233)
        sealed class Esc5
        {
            public string Titulo;
            public bool EnMenu0;
            public Tramo[] Tramos;
            public int TramoSalida = -1;          // -1 = no se sale nunca del menu; si no, la unica salida, en este tramo
            public int TramoSalida2 = -1;         // (o en este: la carga de Rocket League es "atenuada" en la ligera y "juego" en la gris)
            public double SalirMin, SalirMax;     // ...entre estos segundos desde que empieza ese tramo
            public int SilencioDesde = -1;        // el audio de la consola en silencio desde este tramo (-1: suena)
        }

        static int Escenas121(StringBuilder sb, List<Img> banco, List<Img> ex)
        {
            sb.Append("== 5. (1.2.1) Arranque y apagado de la consola: ningun perfil de juego; los juegos de verdad, como antes ==\r\n");
            sb.Append("Salida prudente (MaquinaMenu): sin el menu visto desde que se abrio la app, o 20 s tras su menu de apagado, solo una imagen de\r\n");
            sb.Append("juego clara y seguida 5 s saca del menu (o 60 s de imagen de juego oscura). Con el menu visto, a ~1,2-2,5 s como siempre.\r\n\r\n");
            Func<string, string, List<Img>> de = delegate (string et, string trozo)
            {
                List<Img> r = new List<Img>();
                foreach (Img im in ex) if (im.Etiqueta == et && (trozo == null || Path.GetFileName(im.File).Contains(trozo))) r.Add(im);
                if (r.Count == 0) foreach (Img im in banco) if (im.Etiqueta == et && (trozo == null || Path.GetFileName(im.File).Contains(trozo))) r.Add(im);
                return r;
            };
            List<Img> menu = new List<Img>();
            foreach (Img im in banco) if (im.Etiqueta == "menu" && Path.GetFileName(im.File).Contains("pestanas")) menu.Add(im);
            if (menu.Count == 0) menu = de("menu", null);
            List<Img> arranque = de("arranque", null), bienvenida = de("arranque", "bienvenida"), apagadoTxt = de("apagado", "texto"),
                      apagadoSin = de("apagado", "otrotexto"), menuApagado = de("menuapagado", null), rlPortada = de("juego", "rl-portada"),
                      rlPartido = de("juego", "rl-partido"), rlCarga = de("juegooscuro", null), gtaNoche = de("juego", "gta_145233");
            List<Esc5> esc = new List<Esc5>();
            Esc5 e = new Esc5(); e.Titulo = "Abierta desde la espera con la PS5 arrancando (logo y Bienvenida 14 s) y su menu: no sale"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(arranque, 14, true), new Tramo(menu, 8) }; esc.Add(e);
            e = new Esc5(); e.Titulo = "Abierta con la PS5 arrancando: solo la Bienvenida (la del 21-11 19:09:24-26) 30 s: no sale"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(bienvenida, 30, true) }; esc.Add(e);
            e = new Esc5(); e.Titulo = "Abierta con la PS5 arrancando, su menu y se arranca Rocket League: sale en el juego como siempre"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(arranque, 8, true), new Tramo(menu, 6), new Tramo(rlPortada, 3), new Tramo(rlPartido, 8) };
            e.TramoSalida = 2; e.SalirMin = 0.9; e.SalirMax = 3.0; esc.Add(e);
            e = new Esc5(); e.Titulo = "Abierta con Rocket League ya en marcha (claro): sale a los ~5 s"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(rlPartido, 14, true) }; e.TramoSalida = 0; e.SalirMin = 4.5; e.SalirMax = 7.5; esc.Add(e);
            e = new Esc5(); e.Titulo = "Abierta con un juego oscuro en marcha (GTA VI de noche) 70 s: sale a los ~60 s"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(gtaNoche, 70, true) }; e.TramoSalida = 0; e.SalirMin = 58; e.SalirMax = 63; esc.Add(e);
            e = new Esc5(); e.Titulo = "Menu, menu de apagado y la pantalla de apagarse con su texto 12 s: no sale"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(menu, 4), new Tramo(menuApagado, 3), new Tramo(apagadoTxt, 12) }; esc.Add(e);
            e = new Esc5(); e.Titulo = "Menu, menu de apagado y la pantalla de apagarse con otro texto y el logo (oscura, \"Juego\" hasta la 1.2.09) 12 s: no sale"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(menu, 4), new Tramo(menuApagado, 3), new Tramo(apagadoSin, 12) }; esc.Add(e);
            e = new Esc5(); e.Titulo = "Menu de apagado y una pantalla de apagarse CLARA (la imagen de un juego) en silencio 12 s: no sale"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(menu, 4), new Tramo(menuApagado, 3), new Tramo(rlPortada, 12) }; e.SilencioDesde = 2; esc.Add(e);
            e = new Esc5(); e.Titulo = "Menu de apagado, se cancela y se arranca Rocket League: sale, con prudencia (~5 s)"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(menu, 3), new Tramo(menuApagado, 3), new Tramo(menu, 3), new Tramo(rlPartido, 12) };
            e.TramoSalida = 3; e.SalirMin = 4.5; e.SalirMax = 7.5; esc.Add(e);
            e = new Esc5(); e.Titulo = "Normal: menu y se arranca Rocket League (portada y partido): sale en ~1,2-2,8 s, como antes"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(menu, 6), new Tramo(rlPortada, 3), new Tramo(rlPartido, 8) }; e.TramoSalida = 1; e.SalirMin = 0.9; e.SalirMax = 3.0; esc.Add(e);
            e = new Esc5(); e.Titulo = "Normal: menu, carga oscura de Rocket League y el partido: sale como antes (en la carga o en el partido)"; e.EnMenu0 = true;
            e.Tramos = new Tramo[] { new Tramo(menu, 6), new Tramo(rlCarga, 4), new Tramo(rlPartido, 8) }; e.TramoSalida = 2; e.TramoSalida2 = 1; e.SalirMin = 0.9; e.SalirMax = 3.0; esc.Add(e);
            e = new Esc5(); e.Titulo = "Jugando, el menu 6 s y de vuelta al juego: entra y sale como antes"; e.EnMenu0 = false;
            e.Tramos = new Tramo[] { new Tramo(rlPartido, 6), new Tramo(menu, 6), new Tramo(rlPartido, 8) }; e.TramoSalida = 2; e.SalirMin = 0.9; e.SalirMax = 3.0; esc.Add(e);

            Dictionary<string, LecturaMenu> cache = new Dictionary<string, LecturaMenu>();
            int mal = 0;
            foreach (string var in new[] { "ligera", "gris" })
            {
                sb.Append("-- captura ").Append(var).Append(" --\r\n");
                foreach (Esc5 es in esc)
                {
                    bool vacia = false;
                    foreach (Tramo tr in es.Tramos) if (tr.Imgs.Count == 0) vacia = true;
                    if (vacia) { sb.Append("  (sin imagenes) ").Append(es.Titulo).Append("\r\n"); continue; }
                    string primera = null, porque = null, antes = null;
                    int malas = 0, malasAntes = 0;
                    const int Runs = 6;
                    for (int k = 0; k < Runs; k++)
                    {
                        string ev, pq;
                        bool ok = SimulaApp(es, var, 0.17 * k, cache, false, out ev, out pq);
                        if (!ok) malas++;
                        if (primera == null || (!ok && malas == 1)) { primera = ev; porque = pq; }
                        // (como hasta la 1.2.09: sin la salida prudente)
                        bool okA = SimulaApp(es, var, 0.17 * k, cache, true, out ev, out pq);
                        if (!okA) malasAntes++;
                        if (antes == null || (!okA && malasAntes == 1)) antes = ev;
                    }
                    if (malas > 0) mal++;
                    sb.Append(malas == 0 ? "  OK  " : "  MAL ").Append(es.Titulo).Append("\r\n      ahora: ").Append(primera)
                      .Append(porque != null ? " [" + porque + "]" : "").Append(malas > 0 ? " (MAL en " + malas + " de " + Runs + ")" : "")
                      .Append("\r\n      antes: ").Append(antes).Append(malasAntes > 0 ? " (MAL en " + malasAntes + " de " + Runs + ")" : " (bien)").Append("\r\n");
                }
                sb.Append("\r\n");
            }
            return mal;
        }

        static LecturaMenu Copia(LecturaMenu l)
        {
            LecturaMenu c = new LecturaMenu();
            c.Vista = l.Vista; c.Fuerte = l.Fuerte; c.Hora = l.Hora; c.Ocr = l.Ocr; c.Pantalla = l.Pantalla; c.Frase = l.Frase; c.Porque = l.Porque;
            c.ParecioHora = l.ParecioHora; c.Brillo99 = l.Brillo99; c.Ms = l.Ms; c.Media = l.Media; c.Claros = l.Claros;
            return c;
        }

        // Una escena como la app: capturas cada 0,5 s en el menu (0,25 confirmando), 1 s jugando; el texto de la pantalla en el menu cada 1 s
        // (como MenuPs5Captura); el menu de apagado: salida prudente 20 s y su negro, atenuado. true = lo que pide la escena
        static bool SimulaApp(Esc5 es, string var, double desfase, Dictionary<string, LecturaMenu> cache, bool comoAntes, out string ev, out string porque)
        {
            MaquinaMenu maq = new MaquinaMenu();
            maq.SinPrudencia = comoAntes;
            DateTime t0 = new DateTime(2026, 11, 19, 12, 0, 0);
            bool enMenu = es.EnMenu0;
            double total = 0;
            foreach (Tramo tr in es.Tramos) total += tr.Secs;
            double t = 0.05 + desfase, textoAt = -10, apagadoAt = -1000, busy = 0;
            StringBuilder sb = new StringBuilder();
            porque = null;
            int salidas = 0, entradas = 0;
            bool ok = true;
            while (t < total)
            {
                double acc = 0; int ti = 0;
                while (ti < es.Tramos.Length - 1 && t >= acc + es.Tramos[ti].Secs) { acc += es.Tramos[ti].Secs; ti++; }
                Tramo tramo = es.Tramos[ti];
                double dentro = t - acc;
                Img im = tramo.Imgs[Math.Min(tramo.Imgs.Count - 1, (int)(dentro / 1.5) % tramo.Imgs.Count)];
                Bitmap b = var == "gris" ? im.Gris : im.Ligera;
                bool leer = enMenu && t - textoAt >= 1.0;
                string k = im.File + "|" + var + "|" + enMenu + "|" + leer;
                LecturaMenu l;
                if (!cache.TryGetValue(k, out l))
                {
                    DetectorMenu.OlvidaZonas();
                    l = DetectorMenu.Clasifica(b, im.Now, Consolas.Ps5, enMenu, leer);
                    cache[k] = l;
                }
                if (l.Pantalla != null)
                {
                    textoAt = t;
                    if (RelojPs5.EsMenuApagado(l.Pantalla, Consolas.Ps5)) apagadoAt = t;
                }
                maq.PrudenteHasta = apagadoAt > -100 ? t0.AddSeconds(apagadoAt + 20) : DateTime.MinValue;
                maq.Silencio = es.SilencioDesde >= 0 && ti >= es.SilencioDesde;
                LecturaMenu lx = l;
                if (l.Vista == VistaMenu.Plana && t - apagadoAt < 20) { lx = Copia(l); lx.Vista = VistaMenu.Oscura; }
                int d = maq.Observa(lx, t0.AddSeconds(t), enMenu);
                double ms = Math.Min(l.Ms, 400);   // (la primera lectura con OCR lleva el arranque del OCR de Windows, ~2 s: la app ya lo tiene)
                if (d != 0)
                {
                    enMenu = d > 0;
                    maq.Hecho(t0.AddSeconds(t), enMenu);
                    if (d < 0) salidas++; else entradas++;
                    sb.Append(d > 0 ? "al menu" : "al juego").Append(" a los ").Append(dentro.ToString("0.00", CultureInfo.InvariantCulture))
                      .Append(" s de \"").Append(tramo.Imgs[0].Etiqueta).Append("\" (tramo ").Append(ti + 1).Append(")");
                    if (d < 0)
                    {
                        if (porque == null) porque = maq.Porque;
                        bool bien = (es.TramoSalida == ti || es.TramoSalida2 == ti) && salidas == 1 && dentro >= es.SalirMin && dentro <= es.SalirMax;
                        if (!bien) { ok = false; sb.Append(" MAL"); }
                    }
                    sb.Append("; ");
                    busy = t + 1.0;
                }
                double cad = enMenu ? (maq.Confirmando ? 0.25 : 0.5) : (maq.Confirmando ? 0.25 : 1.0);
                double sig = Math.Max(t + 0.03 + ms / 1000.0, t + cad);
                if (sig < busy) sig = busy;
                t = sig;
            }
            if (es.TramoSalida >= 0 && salidas == 0) { ok = false; sb.Append("sin salir al juego MAL; "); }
            ev = sb.Length == 0 ? "ningun cambio" : sb.ToString();
            return ok;
        }

        static List<Img> Get(Dictionary<string, List<Img>> d, string k) { List<Img> l; return d.TryGetValue(k, out l) ? l : new List<Img>(); }

        static string Pc(int a, int n) { return (n > 0 ? (100.0 * a / n).ToString("0", CultureInfo.InvariantCulture) + "%" : "-").PadLeft(5); }

        sealed class Tramo
        {
            public List<Img> Imgs; public double Secs; public bool Inicio;
            public Tramo(List<Img> i, double s, bool inicio = false) { Imgs = i; Secs = s; Inicio = inicio; }
        }

        // Una escena: tramos seguidos; el estado al empezar (enMenu). Cada captura toma la imagen del tramo de ese momento (una por segundo, en
        // orden, como las copias de la tele). Se repite con 15 desfases de las capturas (0-1,4 s): la primera, con sus cambios; de todas, la
        // media y el peor tiempo hasta decidir. Devuelve 1 si lo de ahora hace algo que no debe en alguna (un cambio de mas o ninguno donde toca)
        static int Escena(StringBuilder sb, Dictionary<string, Res> res, string var, string titulo, bool enMenu0, params Tramo[] tramos)
        {
            foreach (Tramo t in tramos) if (t.Imgs.Count == 0) { sb.Append(titulo).Append(": (sin imagenes)\r\n"); return 0; }
            double total = 0;
            foreach (Tramo t in tramos) total += t.Secs;
            string[] txt = new string[2];
            int malAhora = 0;
            for (int m = 0; m < 2; m++)
            {
                bool ahora = m == 1;
                List<double> aMenu = new List<double>(), aJuego = new List<double>();
                int mal = 0, runs = 15;
                string primera = null;
                for (int k = 0; k < runs; k++)
                {
                    bool bad;
                    string ev = Simula(res, var, enMenu0, tramos, total, ahora, 0.1 * k, aMenu, aJuego, out bad);
                    if (primera == null) primera = ev;
                    if (bad) mal++;
                }
                if (ahora) malAhora = mal;
                txt[m] = primera + "\r\n             " + Lat("al menu", aMenu) + Lat("al juego", aJuego) + (mal > 0 ? "MAL en " + mal + " de " + runs : "bien en las " + runs);
            }
            bool ok = malAhora == 0;
            sb.Append(ok ? "  OK  " : "  MAL ").Append(titulo).Append("\r\n      antes: ").Append(txt[0]).Append("\r\n      ahora: ").Append(txt[1]).Append("\r\n");
            return ok ? 0 : 1;
        }

        static string Lat(string que, List<double> l)
        {
            if (l.Count == 0) return "";
            double s = 0, mx = 0;
            foreach (double v in l) { s += v; mx = Math.Max(mx, v); }
            return que + ": media " + (s / l.Count).ToString("0.00", CultureInfo.InvariantCulture) + " s, peor " + mx.ToString("0.00", CultureInfo.InvariantCulture) + " s; ";
        }

        static string Simula(Dictionary<string, Res> res, string var, bool enMenu0, Tramo[] tramos, double total, bool ahora, double desfase,
                             List<double> aMenu, List<double> aJuego, out bool bad)
        {
            bad = false;
            bool enMenu = enMenu0;
            MaquinaMenu maq = new MaquinaMenu();
            int hA = 0, oA = 0; // (los contadores de antes)
            DateTime t0 = new DateTime(2026, 11, 19, 12, 0, 0);
            double t = 0.05 + desfase, busyHasta = 0;
            bool confirm = false;
            StringBuilder ev = new StringBuilder();
            bool[] cambioEn = new bool[tramos.Length];
            while (t < total)
            {
                // El tramo y la imagen de este momento
                double acc = 0; int ti = 0;
                while (ti < tramos.Length - 1 && t >= acc + tramos[ti].Secs) { acc += tramos[ti].Secs; ti++; }
                Tramo tr = tramos[ti];
                double dentro = t - acc;
                Img im = tr.Imgs[Math.Min(tr.Imgs.Count - 1, (int)dentro % tr.Imgs.Count)];
                Res r = res[im.File + "|" + var];
                double ms = ahora ? (enMenu ? r.AhoraMenu.Ms : r.AhoraJugando.Ms) : r.AntesMs;
                double listo = t + 0.03 + ms / 1000.0; // (la captura del script ~30 ms y la lectura)
                int d;
                if (ahora)
                {
                    LecturaMenu l = enMenu ? r.AhoraMenu : r.AhoraJugando;
                    d = maq.Observa(l, t0.AddSeconds(t), enMenu);
                    confirm = maq.Confirmando;
                }
                else
                {
                    d = MaquinaMenu.Antes(ref hA, ref oA, r.AntesReloj, r.AntesFrase, enMenu);
                    confirm = enMenu ? oA > 0 : hA > 0;
                }
                double tDec = listo;
                if (!ahora && d != 0) tDec = Math.Ceiling(listo / 0.5) * 0.5; // (antes: el siguiente HomeTick)
                if (d != 0)
                {
                    enMenu = d > 0;
                    if (ahora) maq.Hecho(t0.AddSeconds(tDec), enMenu);
                    hA = oA = 0;
                    string et = tr.Imgs[0].Etiqueta;
                    // ¿Lo que tocaba? Al menu en un tramo de menu; al juego en uno de juego o en el negro que va antes de un juego (no al abrir)
                    bool negroAntesDeJuego = et == "negro" && !tr.Inicio && ti + 1 < tramos.Length && EsJuego(tramos[ti + 1].Imgs[0].Etiqueta);
                    bool quiere = EsMenu(et) ? true : EsJuego(et) || negroAntesDeJuego ? false : !enMenu;
                    bool bien = quiere == enMenu && !cambioEn[ti];
                    cambioEn[ti] = true;
                    if (negroAntesDeJuego) cambioEn[ti + 1] = true;
                    if (!bien) bad = true;
                    else (enMenu ? aMenu : aJuego).Add(tDec - acc);
                    ev.Append(enMenu ? "al menu" : "al juego").Append(" a los ").Append((tDec - acc).ToString("0.00", CultureInfo.InvariantCulture))
                      .Append(" s de \"").Append(et).Append("\"").Append(bien ? "" : " MAL").Append("; ");
                    busyHasta = tDec + 1.0; // (el cambio: ~1 s sin capturas)
                    confirm = false;
                }
                // La siguiente captura
                double cad = enMenu ? 0.5 : 1.5;
                if (confirm) cad = ahora ? 0.25 : (enMenu ? 0.5 : 0.4);
                double sig = Math.Max(listo, t + cad);
                if (sig < busyHasta) sig = busyHasta;
                t = sig;
            }
            // Tramos que pedian un cambio y no lo tuvieron
            bool st = enMenu0;
            for (int i = 0; i < tramos.Length; i++)
            {
                string et = tramos[i].Imgs[0].Etiqueta;
                bool? quiere = EsMenu(et) ? true : EsJuego(et) ? (bool?)false : null;
                if (quiere.HasValue && quiere.Value != st)
                {
                    if (!cambioEn[i]) { ev.Append("sin cambio en \"").Append(et).Append("\" MAL; "); bad = true; }
                    st = quiere.Value;
                }
            }
            return ev.Length == 0 ? "ningun cambio" : ev.ToString();
        }

        // ---------------- 3. Xbox, la hora del HOME de la Switch 2, consola=auto ----------------
        static int OtrasConsolas(StringBuilder sb, List<Img> imgs)
        {
            int f = 0;
            sb.Append("== 3. Las otras consolas, con las mismas imagenes ==\r\n");
            sb.Append("Xbox (SIN PROBAR con la consola: las xboxmenu_* son dibujadas, como su Inicio y su guia): la hora del PC en la franja de arriba.\r\n");
            sb.Append("Switch 2: la hora del HOME con la bateria (DetectorMenu.RelojSwitch). consola=auto: la pista que daria cada imagen (MenuPs5Pista).\r\n\r\n");
            foreach (Img im in imgs)
            {
                foreach (string var in new[] { "ligera", "gris" })
                {
                    Bitmap b = var == "gris" ? im.Gris : im.Ligera;
                    DetectorMenu.OlvidaZonas();
                    LecturaMenu x = DetectorMenu.Clasifica(b, im.Now, Consolas.XboxSeries, false, false);
                    DetectorMenu.OlvidaZonas();
                    string rh, ro, rp;
                    bool sw = DetectorMenu.RelojSwitch(b, im.Now, out rh, out ro, out rp);
                    // La pista con consola=auto (como MenuPs5Pista, con otra consola puesta)
                    DetectorMenu.OlvidaZonas();
                    string h; bool miro;
                    string pista = DetectorMenu.PistaReloj(b, im.Now, Consolas.Otra, out h, out miro) ?? "-";
                    // 20-11 (CONSOLA): con la PS5 puesta, nunca la Xbox (su propia hora en la franja de arriba: 04:33 y 04:35 del 20-11)
                    DetectorMenu.OlvidaZonas();
                    string pistaPs5 = DetectorMenu.PistaReloj(b, im.Now, Consolas.Ps5, out h, out miro) ?? "-";
                    string et = im.Etiqueta;
                    bool xMenu = x.Vista == VistaMenu.Menu;
                    bool malX = (et == "xboxmenu" && !xMenu) || ((EsJuego(et) || et == "xboxotra" || et == "negro") && xMenu);
                    bool malS = (et == "sw2home") != sw;
                    bool malP = (et == "menu" && pista != "ps5") || (et == "sw2home" && pista != "switch") || (et == "xboxmenu" && pista != "xbox") || ((EsJuego(et) || et == "xboxotra") && pista != "-");
                    // (el HOME de la Switch 2: "switch", que no suma nada: la pista la dan sus franjas, ObservaConsola)
                    bool malP5 = pistaPs5 == "xbox";
                    if (malX || malS || malP || malP5) f++;
                    if (malX || malS || malP || malP5 || et.StartsWith("xbox") || et == "sw2home" || et.StartsWith("ps5"))
                        sb.Append(malX || malS || malP || malP5 ? "FALLO " : "OK    ").Append(Path.GetFileName(im.File)).Append(" (").Append(var).Append("): Xbox ")
                          .Append(x.Vista).Append(x.Fuerte ? " (fuerte)" : "").Append(x.Hora != null ? " \"" + x.Hora + "\"" : "").Append("; Switch ")
                          .Append(sw ? "HOME \"" + rh + "\"" : "no (" + rp + ")").Append("; pista ").Append(pista).Append("; con la PS5 puesta ").Append(pistaPs5).Append("\r\n");
                }
            }
            sb.Append("(las demas imagenes, sin fallos: ni Xbox ni la hora de la Switch donde no la hay; con la PS5 puesta, ninguna da la Xbox)\r\n\r\n");
            return f;
        }

        // ---------------- 4. consola=auto: que consola es (20-11, agente CONSOLA) ----------------
        // El 20-11 a las 04:33 y 04:35, con la PS5 puesta, la app cambio a la Xbox (SIN PROBAR) y lo guardo en games\console.txt: en las
        // pantallas de la PS5 cuya hora no leia su lector (el avatar junto a la hora en su pantalla de inicio, algo a su derecha...), el
        // de la Xbox la encontraba en la franja de arriba y le daba la pista. Escenas con las capturas de verdad (y las ps5dudoso_*, hechas
        // de ellas), el ritmo de la app y el reloj falso de Consolas, haciendo lo que hace la app: MenuPs5Captura (Clasifica con la consola
        // puesta, la maquina menu <-> juego, el texto de la pantalla en el menu cada 1 s y PistasMenu en cada captura) y ObservaConsola (el
        // texto de la pantalla cada 5 s). games\console.txt en una carpeta de prueba
        sealed class Esc
        {
            public string Titulo, Txt;            // console.txt al empezar (null = sin el)
            public Tramo[] Tramos;
            public string Quiere;                 // la consola al acabar
            public double CambioMinS = -1, CambioMaxS = -1; // si tiene que cambiar: entre estos segundos desde el empiece
            public string Nunca;                  // una consola que no puede salir nunca
        }

        static int Auto(StringBuilder sb, List<Img> imgs)
        {
            sb.Append("== 4. consola=auto: que consola es (20-11, la PS5 tomada por la Xbox) ==\r\n");
            sb.Append("Lo que hace la app con cada captura (Clasifica con la consola puesta, menu <-> juego, el texto de la pantalla y PistasMenu),\r\n");
            sb.Append("con el reloj falso y console.txt en una carpeta de prueba. ps5dudoso_* = pantallas de la PS5 cuya hora no lee su lector\r\n");
            sb.Append("(hechas de las de verdad: el avatar junto a la hora como en su pantalla de inicio, algo a su derecha, la hora en otro sitio).\r\n\r\n");
            Dictionary<string, List<Img>> por = new Dictionary<string, List<Img>>();
            List<Img> noLee = new List<Img>(); // (las de la PS5 cuya hora no lee su lector, en ligera y en gris)
            foreach (Img im in imgs)
            {
                string k = im.Etiqueta == "xboxotra" ? "xboxmenu" : im.Etiqueta == "ps5inicio" ? "ps5dudoso" : im.Etiqueta;
                List<Img> l;
                if (!por.TryGetValue(k, out l)) por[k] = l = new List<Img>();
                l.Add(im);
                if (k == "ps5dudoso")
                {
                    DetectorMenu.OlvidaZonas();
                    bool a = DetectorMenu.Clasifica(im.Ligera, im.Now, Consolas.Ps5, false, false).Vista == VistaMenu.Menu;
                    DetectorMenu.OlvidaZonas();
                    bool b = DetectorMenu.Clasifica(im.Gris, im.Now, Consolas.Ps5, false, false).Vista == VistaMenu.Menu;
                    if (!a && !b) noLee.Add(im);
                }
            }
            List<Img> menu = Get(por, "menu"), dudoso = Get(por, "ps5dudoso"), juego = Get(por, "juego"), oscuro = Get(por, "oscuro"),
                      xbox = Get(por, "xboxmenu"), sw2 = Get(por, "sw2home"), texto = Get(por, "menutexto");
            sb.Append("Pantallas de la PS5 que su lector no lee (ni en ligera ni en gris): ").Append(noLee.Count).Append(" de ").Append(dudoso.Count).Append(":");
            foreach (Img im in noLee) sb.Append(' ').Append(Path.GetFileName(im.File));
            sb.Append("\r\n\r\n");
            List<Esc> escenas = new List<Esc>();
            Esc e = new Esc();
            e.Titulo = "PS5 puesta 3 min, jugando y SOLO pantallas suyas cuya hora no lee su lector (lo del 20-11 04:28-04:35: antes, a la Xbox)";
            e.Txt = "ps5"; e.Quiere = "ps5"; e.Nunca = "xboxseries";
            e.Tramos = new Tramo[] { new Tramo(juego, 20), new Tramo(noLee, 140), new Tramo(juego, 20) };
            escenas.Add(e);
            e = new Esc();
            e.Titulo = "PS5 puesta (console.txt = ps5) 10 min: su menu, sus pantallas que su lector no lee, juegos, la de un juego y el menu atenuado";
            e.Txt = "ps5"; e.Quiere = "ps5"; e.Nunca = "xboxseries";
            e.Tramos = new Tramo[] { new Tramo(dudoso, 60), new Tramo(juego, 60), new Tramo(menu, 20), new Tramo(dudoso, 120), new Tramo(oscuro, 40),
                                     new Tramo(texto, 20), new Tramo(juego, 60), new Tramo(dudoso, 120), new Tramo(menu, 20), new Tramo(dudoso, 80) };
            escenas.Add(e);
            e = new Esc();
            e.Titulo = "PS5 puesta 5 min SOLO con sus pantallas dudosas (ps5dudoso / ps5inicio: las lea o no su lector)";
            e.Txt = "ps5"; e.Quiere = "ps5"; e.Nunca = "xboxseries";
            e.Tramos = new Tramo[] { new Tramo(dudoso, 300) };
            escenas.Add(e);
            e = new Esc();
            e.Titulo = "Xbox puesta por error (console.txt = xboxseries, como el 20-11 05:22-05:28) y delante la PS5: su menu y sus pantallas 3 min";
            e.Txt = "xboxseries"; e.Quiere = "ps5"; e.CambioMinS = 0; e.CambioMaxS = 30;
            e.Tramos = new Tramo[] { new Tramo(menu, 60), new Tramo(dudoso, 60), new Tramo(juego, 60) };
            escenas.Add(e);
            e = new Esc();
            e.Titulo = "Sin console.txt (otra) y la PS5: su menu 2 min";
            e.Txt = null; e.Quiere = "ps5"; e.CambioMinS = 0; e.CambioMaxS = 30; e.Nunca = "xboxseries";
            e.Tramos = new Tramo[] { new Tramo(menu, 60), new Tramo(dudoso, 60) };
            escenas.Add(e);
            e = new Esc();
            e.Titulo = "PS5 puesta y en su lugar una Xbox (el Inicio y la guia dibujados, SIN PROBAR) 3 min: a la Xbox por sus pantallas, no por la hora, y no antes de 20 s";
            e.Txt = "ps5"; e.Quiere = "xboxseries"; e.CambioMinS = Consolas.SeparacionSinProbarS; e.CambioMaxS = 120;
            e.Tramos = new Tramo[] { new Tramo(xbox, 180) };
            escenas.Add(e);
            e = new Esc();
            e.Titulo = "PS5 puesta y el HOME de la Switch 2 2 min (sin su referencia en la prueba): nunca la Xbox";
            e.Txt = "ps5"; e.Quiere = null; e.Nunca = "xboxseries";
            e.Tramos = new Tramo[] { new Tramo(sw2, 120) };
            escenas.Add(e);

            Dictionary<string, LecturaMenu> cache = new Dictionary<string, LecturaMenu>();
            Dictionary<string, string> textos = new Dictionary<string, string>();
            string tmp = Path.Combine(Path.GetTempPath(), "prisma_test_auto_" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(tmp);
            string viejaCarpeta = Consolas.CarpetaDePrueba(tmp);
            Action<string> savedHook = Log.Hook;
            int mal = 0;
            try
            {
                foreach (string var in new[] { "ligera", "gris" })
                {
                    sb.Append("-- captura ").Append(var).Append(" --\r\n");
                    foreach (Esc es in escenas)
                    {
                        bool vacia = false;
                        foreach (Tramo tr in es.Tramos) if (tr.Imgs.Count == 0) vacia = true;
                        if (vacia) { sb.Append("  (sin imagenes) ").Append(es.Titulo).Append("\r\n"); continue; }
                        string res;
                        bool ok = UnaEscena(es, var, tmp, cache, textos, out res);
                        if (!ok) mal++;
                        sb.Append(ok ? "  OK  " : "  MAL ").Append(es.Titulo).Append("\r\n").Append(res);
                    }
                    sb.Append("\r\n");
                }
            }
            finally
            {
                Log.Hook = savedHook;
                Consolas.Reloj = null;
                Consolas.CarpetaDePrueba(viejaCarpeta);
                Consolas.Reiniciar("auto");
                try { Directory.Delete(tmp, true); } catch { }
            }
            return mal;
        }

        static bool UnaEscena(Esc es, string var, string tmp, Dictionary<string, LecturaMenu> cache, Dictionary<string, string> textos, out string res)
        {
            StringBuilder r = new StringBuilder();
            string txt = Path.Combine(tmp, "console.txt");
            foreach (string f in Directory.GetFiles(tmp)) File.Delete(f);
            if (es.Txt != null) File.WriteAllText(txt, es.Txt);
            DateTime t0 = new DateTime(2026, 11, 20, 4, 30, 0);
            DateTime now = t0;
            Consolas.Reloj = delegate { return now; };
            List<string> log = new List<string>();
            Log.Hook = delegate (string m) { if (m.StartsWith("Consola")) log.Add(m); };
            Consolas.Reiniciar("auto");
            ConsolaPerfil inicio = Consolas.Actual;
            MaquinaMenu maq = new MaquinaMenu();
            PistasMenu pm = new PistasMenu();
            bool enMenu = false;
            double total = 0;
            foreach (Tramo tr in es.Tramos) total += tr.Secs;
            double t = 0.05, textoAt = -10, obsAt = -10;
            Dictionary<string, int> cuenta = new Dictionary<string, int>();
            List<string> cambios = new List<string>();
            double primerCambio = -1;
            bool nunca = false;
            ConsolaPerfil antes = inicio;
            while (t < total)
            {
                now = t0.AddSeconds(t);
                double acc = 0; int ti = 0;
                while (ti < es.Tramos.Length - 1 && t >= acc + es.Tramos[ti].Secs) { acc += es.Tramos[ti].Secs; ti++; }
                Tramo tramo = es.Tramos[ti];
                Img im = tramo.Imgs[Math.Min(tramo.Imgs.Count - 1, (int)(t - acc) % tramo.Imgs.Count)];
                Bitmap b = var == "gris" ? im.Gris : im.Ligera;
                // La hora "del PC" es la del nombre de la imagen (la de su reloj) mas lo que va de tramo: se usa la de la imagen
                DateTime hImg = im.Now;
                ConsolaPerfil c = Consolas.Actual;
                LecturaMenu l = null;
                if (c.MenuPropio)
                {
                    // (como MenuPs5Captura: Clasifica con la consola puesta; en el menu, la pantalla entera cada 1 s)
                    string k = im.File + "|" + var + "|" + c.Clave + "|" + enMenu;
                    if (!cache.TryGetValue(k, out l))
                    {
                        DetectorMenu.OlvidaZonas();
                        l = DetectorMenu.Clasifica(b, hImg, c, enMenu, enMenu);
                        cache[k] = l;
                    }
                    if (l.Pantalla != null && t - textoAt >= 1.0) { textoAt = t; Consolas.VistoTexto(l.Pantalla); }
                    int d = maq.Observa(l, now, enMenu);
                    if (d != 0) { enMenu = d > 0; maq.Hecho(now, enMenu); }
                }
                // (ObservaConsola: la Switch 2 no; el texto de la pantalla cada 5 s los 3 primeros minutos, luego cada 30 s)
                if (!c.TieneHome && t - obsAt >= (t < 180 ? 5 : 30))
                {
                    obsAt = t;
                    string k = im.File + "|" + var;
                    string s;
                    if (!textos.TryGetValue(k, out s)) textos[k] = s = HomeLabel.ScreenText(b);
                    Consolas.VistoTexto(s);
                }
                // (PistasMenu: el ritmo con el reloj de la escena; la hora de la imagen para leer la de la consola)
                DetectorMenu.OlvidaZonas();
                string q = pm.Observa(b, now, hImg, Consolas.Actual, l, false);
                if (q != null) { int n; cuenta.TryGetValue(q, out n); cuenta[q] = n + 1; }
                ConsolaPerfil ahora = Consolas.Actual;
                if (ahora != antes)
                {
                    cambios.Add(antes.Clave + " -> " + ahora.Clave + " a los " + t.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                    if (primerCambio < 0) primerCambio = t;
                    if (es.Nunca != null && ahora.Clave == es.Nunca) nunca = true;
                    antes = ahora;
                    maq.Reiniciar(); // (CambioDeConsola)
                    enMenu = false;
                }
                double cad = enMenu ? 0.5 : 1.5;
                if (maq.Confirmando) cad = 0.25;
                t += cad;
            }
            string fin = Consolas.Actual.Clave;
            string enTxt = File.Exists(txt) ? File.ReadAllText(txt).Trim() : "(nada)";
            bool ok = !nunca;
            if (es.Quiere != null && fin != es.Quiere) ok = false;
            if (es.Quiere != null && (enTxt != es.Quiere && !(es.Txt == null && enTxt == "(nada)" && es.Quiere == "otra"))) ok = false;
            if (es.CambioMinS >= 0 && (primerCambio < es.CambioMinS || primerCambio > es.CambioMaxS || cambios.Count != 1)) ok = false;
            if (es.CambioMinS < 0 && es.Quiere != null && es.Quiere == inicio.Clave && cambios.Count > 0) ok = false;
            StringBuilder pc = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in cuenta) pc.Append(pc.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(kv.Value);
            r.Append("      empieza ").Append(inicio.Clave).Append(", acaba ").Append(fin).Append(", console.txt ").Append(enTxt).Append("; cambios: ")
             .Append(cambios.Count == 0 ? "ninguno" : string.Join("; ", cambios.ToArray())).Append("; pistas de PistasMenu: ").Append(pc.Length > 0 ? pc.ToString() : "ninguna")
             .Append("; puntos al acabar: ").Append(Consolas.PuntosTexto()).Append("\r\n");
            foreach (string m in log) if (m.StartsWith("Consola: ahora") || m.StartsWith("Consola: la imagen apunta")) r.Append("        log: ").Append(m.Length > 230 ? m.Substring(0, 230) + "..." : m).Append("\r\n");
            res = r.ToString();
            return ok;
        }

        // ---------------- Imagenes como las de la app ----------------
        // La captura ligera del script de mpv: 1 de cada 3 pixeles de 4K; de otra cosa, a 1280x720
        public static Bitmap Ligera(Bitmap src)
        {
            int w = 1280, h = 720;
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                bool k3 = src.Width == 3840;
                g.InterpolationMode = k3 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = k3 ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            return b;
        }

        // La del motor con la captura en yuy2: la Y en grises, en rango limitado (16-235)
        public static Bitmap Gris(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            BitmapData s = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                byte[] row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(s.Scan0.ToInt64() + (long)y * s.Stride), row, 0, row.Length);
                    for (int x = 0; x < w; x++)
                    {
                        int o = x * 4, v = 16 + ((row[o] * 29 + row[o + 1] * 150 + row[o + 2] * 77) >> 8) * 219 / 255;
                        row[o] = row[o + 1] = row[o + 2] = (byte)v;
                    }
                    Marshal.Copy(row, 0, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row.Length);
                }
            }
            finally { src.UnlockBits(s); b.UnlockBits(d); }
            return b;
        }
    }
}
