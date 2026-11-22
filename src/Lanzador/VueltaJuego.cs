// VueltaJuego.cs - 22-11 (1.2.2, agente PERFILES). El usuario: "Me he salido de gta 6 a menu y de ahi a bloodborne y se pensaba que
// seguia en gta 6 ... tiene que ser infalible con cualquier juego y consola".
//
// Lo que paso (log del 22-11, PS5):
//  1. 03:59:36 GTA VI en marcha -> menu; el foco pasa a Bloodborne y se arranca. Al salir del menu la app siguio con GTA VI ("sigue el de
//     antes salvo que se vea arrancar el otro") y esperaba la prueba del arranque (negro liso, sin señal, su portada) 8 s de reloj... pero
//     mientras Magpie preparaba (passesBusy, 11 s) no se pedia ni una captura: a los 8 s "no se arranco". Ahora (VueltaOArranque):
//      - se sigue capturando mientras Magpie prepara (solo para el menu y esta comprobacion: CapturaPreparando);
//      - las capturas que sacaron del menu ya cuentan (pruebas de enseguida: la misma imagen que antes del menu = se ha vuelto; el negro
//        que saco del menu = se arranca otro);
//      - la imagen de antes del menu son las ULTIMAS capturas del juego (antes la ultima captura fuera del menu, que podia ser ya el menu
//        apareciendo) y se compara sin el brillo (el menu se funde);
//      - el plazo cuenta segundos MIRADOS (25 s), no de reloj; si la prueba llega con Magpie ya listo, se cambia al juego bueno entonces.
//     Una portada nueva solo se memoriza si se vio arrancar un juego (negro, sin señal o su portada) para el juego decidido: 04:04:08 se
//     guardo como "portada" de Bloodborne una imagen quieta del juego al volver a el (no hubo arranque).
//  2. Nombres con basura pegada (la ™ / ® de la etiqueta leida como letras): "Bloodbornevy", "BloodborneJY", "Bloodborney", "Rocket
//     Leagueñ", "Rocket LeagueF"... "PS5: Bloodborney" se creo con uno de ellos. Ahora (NombresConsola) el nombre sale del CONSENSO de las
//     lecturas: la basura cambia de una lectura a otra y el nombre no; y al abrir la app los perfiles, iconos y portadas con basura pasan
//     a su nombre bueno (Migra).
//  3. Al despertar la PS5 (abierta desde la espera): "Te damos la bienvenida de nuevo a PlayStation | ¿Quien esta usando...?" (con la hora
//     arriba: contaba como el menu de verdad) y luego sus particulas oscuras: a los 1,7 s "fuera del menu" con el perfil de la consola,
//     DLSS 5 y generacion 10 s. Ahora la Bienvenida / elegir usuario son "pantalla de la consola" y ponen la salida prudente hasta ver el
//     menu de verdad (solo una imagen de juego clara y seguida saca).
// C# 5
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ConsoleEnhancer
{
    // ===================================================================
    //  Nombres: basura pegada, consenso de lecturas, migracion de lo guardado. Pura (sin la app)
    // ===================================================================
    static class NombresConsola
    {
        public const int SufijoMax = 3;      // caracteres de basura pegada a la ultima palabra (la ™ / ® leida: "vy", "JY", "Jbf", "ñ")

        static readonly Regex RomanoRx = new Regex(@"^[IVXivx]{1,4}$", RegexOptions.CultureInvariant);

        static string[] PalabrasCrudas(string s)
        {
            string t = Regex.Replace(JuegoConsola.SinTag(s ?? "").Trim(), @"\s+", " ");
            return t.Length == 0 ? new string[0] : t.Split(' ');
        }

        static int LetrasDe(string w) { int n = 0; foreach (char ch in w) if (char.IsLetter(ch)) n++; return n; }
        static bool ConCifras(string w) { foreach (char ch in w) if (char.IsDigit(ch)) return true; return false; }

        // ¿largo es corto con basura pegada al final? La ultima palabra de corto (4 letras o mas, sin cifras) es el principio de la de largo,
        // que tiene 1-3 caracteres de mas ("Bloodborne" -> "Bloodbornevy", "Rocket League" -> "Rocket Leagueñ"), o largo lleva una palabrita
        // de mas de 1-2 caracteres que no es un numero ni un numero romano ("Bloodborne a"; "Destiny 2" no). Lo demas, igual
        public static bool BasuraPegada(string largo, string corto) { string s; return BasuraPegada(largo, corto, out s); }

        public static bool BasuraPegada(string largo, string corto, out string sufijo)
        {
            sufijo = null;
            string[] a = PalabrasCrudas(largo), b = PalabrasCrudas(corto);
            if (a.Length == 0 || b.Length == 0) return false;
            string ub = b[b.Length - 1];
            if (LetrasDe(ub) < 4 || ConCifras(ub)) return false;
            if (a.Length == b.Length)
            {
                for (int i = 0; i < b.Length - 1; i++) if (GameNames.Key(a[i]) != GameNames.Key(b[i])) return false;
                string ua = a[a.Length - 1];
                if (ua.Length <= ub.Length || ua.Length - ub.Length > SufijoMax) return false;
                if (!ua.StartsWith(ub, StringComparison.OrdinalIgnoreCase)) return false;
                string s = ua.Substring(ub.Length);
                bool cifras = true;
                foreach (char ch in s) if (!char.IsDigit(ch)) cifras = false;
                if (cifras || RomanoRx.IsMatch(s)) return false;   // ("Spider-Man2", "Rocky II" pegado: numero, no basura)
                sufijo = s;
                return true;
            }
            if (a.Length == b.Length + 1)
            {
                for (int i = 0; i < b.Length; i++) if (GameNames.Key(a[i]) != GameNames.Key(b[i])) return false;
                string extra = a[a.Length - 1];
                if (extra.Length > 2 || ConCifras(extra) || RomanoRx.IsMatch(extra)) return false;
                sufijo = " " + extra;
                return true;
            }
            return false;
        }

        // ¿Ese sufijo no puede ser el final de la palabra? Una mayuscula tras minuscula ("BloodborneJY", "Rocket LeagueF"), una letra con
        // tilde tras una palabra sin ninguna ("Rocket Leagueñ", "Leagueõ"), simbolos o cifras
        public static bool SufijoRaro(string sufijo, string corto)
        {
            if (string.IsNullOrEmpty(sufijo)) return false;
            string[] b = PalabrasCrudas(corto);
            string ub = b.Length > 0 ? b[b.Length - 1] : "";
            char fin = ub.Length > 0 ? ub[ub.Length - 1] : ' ';
            bool asciiBase = true;
            foreach (char ch in ub) if (ch > 127) asciiBase = false;
            string s = sufijo.TrimStart(' ');
            // (una palabrita de mas: una letra suelta, simbolos o mayusculas y minusculas mezcladas; "TY" / "JY" sueltas deciden las lecturas)
            if (sufijo.StartsWith(" ")) return s.Length <= 1 || !Regex.IsMatch(s, @"^\p{L}+$") || UpperMix(s);
            foreach (char ch in s)
            {
                if (!char.IsLetter(ch)) return true;
                if (char.IsUpper(ch) && char.IsLower(fin)) return true;
                if (ch > 127 && asciiBase) return true;
            }
            return false;
        }

        static bool UpperMix(string s) { bool up = false, low = false; foreach (char ch in s) { if (char.IsUpper(ch)) up = true; if (char.IsLower(ch)) low = true; } return up && low; }

        // La ™ pegada leida como letras: "TY", "JY", "vy", "y", "TM" (la M leida como Y)
        static readonly Regex TmRx = new Regex(@"^(?:[TtJjVv]?[YyMm])$", RegexOptions.CultureInvariant);

        // ¿Ese sufijo es basura probable? (raro, o la ™ leida como letras)
        public static bool BasuraProbable(string sufijo, string corto)
        {
            if (string.IsNullOrEmpty(sufijo)) return false;
            return SufijoRaro(sufijo, corto) || TmRx.IsMatch(sufijo.Trim());
        }

        static string UltimaPalabraLetras(string s)
        {
            string[] w = PalabrasCrudas(s);
            if (w.Length == 0) return "";
            return Regex.Replace(w[w.Length - 1], @"[^\p{L}]", "");
        }

        // De un par (largo = corto + basura pegada): ¿el nombre bueno es el largo? El diccionario de Windows si lo sabe (la palabra larga
        // existe y la corta no: "Leag" -> "League"; al reves: "Leaguen" -> "League"); un sufijo raro (SufijoRaro), el corto; si no, el largo
        // solo si se lee claramente mas que el corto y que las demas basuras del corto juntas (una basura cambia de lectura a lectura, el
        // final de verdad no). A igualdad, el corto: la etiqueta cortada al final de una palabra (1-3 letras) no se ha visto; la ™ / ® si
        public static bool GanaLargo(string largo, string corto, int vecesLargo, int vecesCorto, int otrasBasuras)
        {
            string suf;
            if (!BasuraPegada(largo, corto, out suf)) return true;
            bool? vl = Ortografia.Valida(UltimaPalabraLetras(largo)), vc = Ortografia.Valida(UltimaPalabraLetras(corto));
            if (!suf.StartsWith(" "))
            {
                if (vl == true && vc != true) return true;
                if (vc == true && vl == false) return false;
            }
            if (SufijoRaro(suf, corto)) return false;
            // (un sufijo que no parece la ™: mas bien el corto es la etiqueta cortada, "Rocket Leag": a igualdad, el largo)
            if (!BasuraProbable(suf, corto)) return vecesLargo >= vecesCorto;
            return vecesLargo > vecesCorto + otrasBasuras;
        }

        sealed class Cand
        {
            public string Nombre;
            public string Clave;
            public int Veces;        // lecturas iguales (por su clave)
            public int Ultima;       // la ultima lectura (orden)
            public bool Sintetico;   // la base comun de varias lecturas con basura distinta (no leida tal cual)
            public bool Sucio;       // es otro con basura pegada (gana el otro)
            public bool Cubierto;    // es un trozo de otro (gana el otro)
            public int Apoyo;        // lecturas que explica
            public bool Conocido;
        }

        // El nombre de consenso de las lecturas de un mismo foco (en orden). Puntos, por orden: limpio (JuegoConsola.Limpio); no es otro con
        // basura pegada (GanaLargo); no es un trozo de otro limpio (JuegoConsola.Parcial: "Rocket agu" de "Rocket League"); las lecturas
        // que explica (las suyas, sus basuras y sus trozos); el de un perfil que ya existe (conocido); palabras del diccionario; la ultima.
        // Varias lecturas con basura distinta y la base comun sin leer tal cual ("BloodborneJY", "Bloodbornevy") dan la base. apoyo = las
        // lecturas que explica
        public static string Consenso(List<string> nombres, Func<string, bool> conocido, out int apoyo)
        {
            apoyo = 0;
            if (nombres == null) return null;
            Dictionary<string, Cand> porClave = new Dictionary<string, Cand>();
            List<Cand> cands = new List<Cand>();
            for (int i = 0; i < nombres.Count; i++)
            {
                string n = nombres[i];
                if (string.IsNullOrEmpty(n)) continue;
                string k = GameNames.Key(n);
                if (k.Length == 0) continue;
                Cand c;
                if (!porClave.TryGetValue(k, out c)) { c = new Cand(); c.Clave = k; porClave[k] = c; cands.Add(c); }
                c.Nombre = n; c.Veces++; c.Ultima = i;
            }
            if (cands.Count == 0) return null;
            // La base comun de lecturas con basura distinta (sin leerse tal cual): "Bloodbornevy" + "BloodborneJY" -> "Bloodborne"
            int nBase = cands.Count;
            for (int i = 0; i < nBase; i++)
                for (int j = i + 1; j < nBase; j++)
                {
                    string b = BaseComun(cands[i].Nombre, cands[j].Nombre);
                    if (b == null) continue;
                    string kb = GameNames.Key(b);
                    if (porClave.ContainsKey(kb)) continue;
                    Cand s = new Cand(); s.Nombre = b; s.Clave = kb; s.Sintetico = true; s.Ultima = Math.Max(cands[i].Ultima, cands[j].Ultima);
                    porClave[kb] = s; cands.Add(s);
                }
            foreach (Cand c in cands) if (conocido != null) { try { c.Conocido = conocido(c.Nombre); } catch { } }
            // Pares largo = corto + basura: quien gana
            foreach (Cand e in cands)
                foreach (Cand b in cands)
                {
                    if (e == b || !BasuraPegada(e.Nombre, b.Nombre)) continue;
                    int otras = 0;
                    foreach (Cand o in cands) if (o != e && o != b && BasuraPegada(o.Nombre, b.Nombre)) otras += o.Veces;
                    // (el de un perfil que ya existe: el corto gana salvo el diccionario; el largo, como una lectura mas: un perfil guardado
                    // con basura, "PS5: Bloodborney", no gana a tres lecturas limpias)
                    bool largo = b.Conocido && !e.Conocido ? GanaLargoConocido(e.Nombre, b.Nombre) :
                                 GanaLargo(e.Nombre, b.Nombre, e.Veces + (e.Conocido && !b.Conocido ? 1 : 0), b.Veces, otras);
                    if (largo) b.Cubierto = true; else e.Sucio = true;
                }
            // Trozos de otro limpio (la etiqueta entrando, saliendo o fundiendose)
            foreach (Cand c in cands)
                foreach (Cand o in cands)
                    if (c != o && JuegoConsola.Limpio(o.Nombre) && !o.Sucio && JuegoConsola.Parcial(c.Nombre, o.Nombre) && !BasuraPegada(o.Nombre, c.Nombre))
                    { c.Cubierto = true; break; }
            // Lo que explica cada uno: sus lecturas, las de sus basuras (si gana el) y las de sus trozos
            foreach (Cand c in cands)
            {
                int a = c.Veces;
                foreach (Cand o in cands)
                {
                    if (o == c) continue;
                    if (o.Sucio && BasuraPegada(o.Nombre, c.Nombre)) a += o.Veces;
                    else if (JuegoConsola.Parcial(o.Nombre, c.Nombre) && !BasuraPegada(c.Nombre, o.Nombre)) a += o.Veces;
                }
                c.Apoyo = a;
            }
            Cand best = null;
            int[] bs = null;
            foreach (Cand c in cands)
            {
                int[] s = { JuegoConsola.Limpio(c.Nombre) ? 1 : 0, c.Sucio ? 0 : 1, c.Cubierto ? 0 : 1, c.Apoyo, c.Conocido ? 1 : 0,
                            JuegoConsola.EnDiccionario(c.Nombre) ? 1 : 0, c.Sintetico ? 0 : 1, c.Ultima };
                bool mejor = bs == null;
                for (int x = 0; !mejor && x < s.Length; x++)
                {
                    if (s[x] == bs[x]) continue;
                    mejor = s[x] > bs[x];
                    break;
                }
                if (mejor) { best = c; bs = s; }
            }
            apoyo = best.Apoyo;
            return best.Nombre;
        }

        // El corto es el nombre de un perfil que ya existe y el largo una lectura con basura pegada: gana el conocido salvo que el
        // diccionario diga que la palabra larga existe y la corta no
        public static bool GanaLargoConocido(string largo, string corto)
        {
            string suf;
            if (!BasuraPegada(largo, corto, out suf)) return true;
            if (suf.StartsWith(" ")) return false;
            bool? vl = Ortografia.Valida(UltimaPalabraLetras(largo)), vc = Ortografia.Valida(UltimaPalabraLetras(corto));
            return vl == true && vc != true;
        }

        // La base comun de dos lecturas con basura distinta pegada a la misma palabra ("Bloodbornevy" / "BloodborneJY" -> "Bloodborne",
        // "Rocket Leagueñ" / "Rocket LeagueF" -> "Rocket League"), o null. Las demas palabras iguales; la ultima de cada una = la base
        // (4 letras o mas) + 1-3 caracteres distintos, los dos basura probable (BasuraProbable: "Bloodborne" / "Bloodbornc:v" NO dan
        // "Bloodborn": la "e" no es basura)
        public static string BaseComun(string x, string y)
        {
            string[] a = PalabrasCrudas(x), b = PalabrasCrudas(y);
            if (a.Length == 0 || a.Length != b.Length) return null;
            for (int i = 0; i < a.Length - 1; i++) if (GameNames.Key(a[i]) != GameNames.Key(b[i])) return null;
            string ua = a[a.Length - 1], ub = b[b.Length - 1];
            int n = 0;
            while (n < ua.Length && n < ub.Length && char.ToLowerInvariant(ua[n]) == char.ToLowerInvariant(ub[n])) n++;
            if (n == ua.Length || n == ub.Length) return null;    // (uno es el principio del otro: eso es BasuraPegada, sin base nueva)
            string pre = ua.Substring(0, n);
            if (LetrasDe(pre) < 4 || ConCifras(pre) || !char.IsLetter(pre[pre.Length - 1])) return null;
            if (ua.Length - n > SufijoMax || ub.Length - n > SufijoMax) return null;
            string cand = string.Join(" ", a, 0, a.Length - 1);
            cand = (cand.Length > 0 ? cand + " " : "") + pre;
            if (!BasuraProbable(ua.Substring(n), cand) || !BasuraProbable(ub.Substring(n), cand)) return null;
            string tag = JuegoConsola.TagConsola(x);
            return tag != null ? tag + ": " + cand : cand;
        }

        // ---------------- Migracion de lo guardado (al abrir la app) ----------------
        // Los nombres guardados de una consola (perfiles, iconos, portadas, el ultimo juego) con basura pegada pasan a su nombre bueno: el de
        // otro nombre guardado del que son "basura pegada" (y el diccionario no dice lo contrario), la base comun de dos o mas con basura
        // distinta ("PS5: Bloodborney" del perfil y "PS5: Bloodbornevy" del icono -> "PS5: Bloodborne") o, con un sufijo raro (una
        // mayuscula tras minuscula, una tilde suelta, un simbolo), el nombre sin el. Devuelve viejo -> nuevo
        public static Dictionary<string, string> Migracion(List<string> nombres)
        {
            Dictionary<string, string> r = new Dictionary<string, string>();
            List<string> l = new List<string>();
            foreach (string n in nombres) if (!string.IsNullOrEmpty(n) && JuegoConsola.TagConsola(n) != null && !l.Contains(n)) l.Add(n);
            // 1. La base comun de dos con basura distinta
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    if (JuegoConsola.TagConsola(l[i]) != JuegoConsola.TagConsola(l[j])) continue;
                    string b = BaseComun(l[i], l[j]);
                    if (b == null) continue;
                    if (!r.ContainsKey(l[i])) r[l[i]] = b;
                    if (!r.ContainsKey(l[j])) r[l[j]] = b;
                }
            // 2. Uno que es otro con basura pegada
            foreach (string e in l)
            {
                if (r.ContainsKey(e)) continue;
                foreach (string b in l)
                {
                    if (b == e || JuegoConsola.TagConsola(b) != JuegoConsola.TagConsola(e) || !BasuraPegada(e, b)) continue;
                    if (GanaLargoConocido(e, b)) continue;
                    r[e] = r.ContainsKey(b) ? r[b] : b;
                    break;
                }
            }
            // 3. Solo, con un sufijo raro
            foreach (string e in l)
            {
                if (r.ContainsKey(e)) continue;
                string limpio = SinBasuraRara(e);
                if (limpio != null && limpio != e) r[e] = limpio;
            }
            // (las cadenas: a -> b -> c = a -> c)
            List<string> ks = new List<string>(r.Keys);
            foreach (string k in ks)
            {
                string v = r[k];
                for (int g = 0; g < 5 && r.ContainsKey(v) && r[v] != v; g++) v = r[v];
                if (v == k) r.Remove(k); else r[k] = v;
            }
            return r;
        }

        // El nombre sin una basura rara pegada a la ultima palabra ("PS5: Rocket LeagueF" -> "PS5: Rocket League"), o null si no la lleva.
        // Solo lo claro (SufijoRaro y la palabra que queda de 4 letras o mas), y no si el diccionario dice que la palabra entera existe
        public static string SinBasuraRara(string nombre)
        {
            string tag = JuegoConsola.TagConsola(nombre);
            string[] w = PalabrasCrudas(nombre);
            if (w.Length == 0) return null;
            string u = w[w.Length - 1];
            for (int k = 1; k <= SufijoMax && k < u.Length - 3; k++)
            {
                string pre = u.Substring(0, u.Length - k), suf = u.Substring(u.Length - k);
                string corto = string.Join(" ", w, 0, w.Length - 1);
                corto = (corto.Length > 0 ? corto + " " : "") + pre;
                string s;
                if (!BasuraPegada(string.Join(" ", w), corto, out s)) continue;
                if (!SufijoRaro(suf, corto)) continue;
                // (todo el sufijo raro desde su primer caracter raro: "LeagueJbf" -> "League", no "LeagueJ")
                if (Ortografia.Valida(Regex.Replace(u, @"[^\p{L}]", "")) == true) return null;
                return tag != null ? tag + ": " + corto : corto;
            }
            return null;
        }
    }

    // ===================================================================
    //  La Bienvenida / elegir usuario de la consola al encenderla o despertarla (con la hora arriba: contaba como el menu de verdad y sus
    //  particulas oscuras de despues sacaban del menu). Pura: la usan MenuPs5Captura y la prueba (src\PruebaDeteccion.cs, seccion 5)
    // ===================================================================
    sealed class EstadoBienvenida
    {
        public DateTime Hasta = DateTime.MinValue;   // salida prudente hasta (o hasta ver el menu de verdad); MinValue = nada
        DateTime vista = DateTime.MinValue;          // la ultima lectura de la pantalla con la Bienvenida
        public const double DuraS = 120;

        // Una captura en el menu (ya clasificada). Si es la Bienvenida con la hora (Menu), pasa a MenuTexto "bienvenida". Devuelve lo que
        // ir al log (o null)
        public string Observa(LecturaMenu l, DateTime now)
        {
            if (l == null) return null;
            string log = null;
            if (Hasta != DateTime.MinValue && now > Hasta) Hasta = DateTime.MinValue;
            if (l.Pantalla != null)
            {
                if (JuegoConsola.EsBienvenida(l.Pantalla))
                {
                    if (Hasta == DateTime.MinValue)
                        log = "Consola: la Bienvenida / elegir usuario: no es el menu; hasta ver el menu de verdad (o " + DuraS.ToString("0", CultureInfo.InvariantCulture) +
                              " s) solo saca de el una imagen de juego clara y seguida";
                    Hasta = now.AddSeconds(DuraS);
                    vista = now;
                }
                else if (l.Vista == VistaMenu.Menu && Hasta != DateTime.MinValue)
                {
                    log = "Consola: el menu de verdad tras la Bienvenida (la hora \"" + l.Hora + "\")";
                    Hasta = DateTime.MinValue;
                }
            }
            else if (l.Vista == VistaMenu.Menu && l.Hora == "pestañas" && Hasta != DateTime.MinValue)
            {
                log = "Consola: el menu de verdad tras la Bienvenida (sus pestañas)";
                Hasta = DateTime.MinValue;
            }
            // (entre dos lecturas de la pantalla, la de hace 1,6 s como mucho)
            if (l.Vista == VistaMenu.Menu && Hasta != DateTime.MinValue && (now - vista).TotalSeconds < 1.6 && l.Hora != "pestañas")
            {
                l.Vista = VistaMenu.MenuTexto;
                l.Frase = "bienvenida";
            }
            return log;
        }
    }

    // ===================================================================
    //  Tras salir del menu con el foco en OTRO juego que el que estaba en marcha: ¿se ha vuelto al de antes o se ha arrancado el del
    //  foco? Pura (el reloj y las capturas los da la app o la prueba). Pruebas:
    //   - la de VOLVER: una imagen clara como las de antes del menu (sin el brillo: DistNorm, y en crudo) -> el de antes
    //   - las de ARRANCAR: negro liso 1,2 s (si lo de antes del menu no era oscuro), "sin señal" 0,8 s (la consola cambia de modo de video al
    //     arrancar otro juego), la portada conocida del foco (o de otro juego), o una imagen quieta con dibujo que no es la de antes (la
    //     portada nueva del foco) seguida de su carga a oscuras
    //   - sin pruebas en 25 s MIRADOS (capturas con menos de 2 s entre ellas) o 90 s de reloj: el de antes
    // ===================================================================
    sealed class VueltaOArranque
    {
        public const double MismaNorm = 13.0;    // DistNorm 16x9 por debajo (y MismaCruda en crudo): la misma escena que antes del menu
        public const double MismaCruda = 30.0;
        public const double NegroS = 1.2, SinSenalS = 0.8, OscuroTrasPortadaS = 1.0;
        public const double ObservarS = 25, RelojMaxS = 90, HuecoMaxS = 2.0;
        public const double DistintaNorm = 22.0; // una portada (quieta) "no es la de antes" por encima de esto (normalizada)

        public readonly string EnMarcha, Candidato;
        readonly List<double[]> antes = new List<double[]>();
        readonly bool antesOscuro;
        readonly DateTime inicio;
        DateTime ultima = DateTime.MinValue;
        double observado;
        DateTime negroDesde = DateTime.MinValue, sinSenalDesde = DateTime.MinValue, oscuroDesde = DateTime.MinValue;
        double[] quietaPrev;
        DateTime quietaDesde = DateTime.MinValue;
        public double[] PortadaVista;    // la imagen quieta con dibujo vista (la portada del que se arranca, si se arranca)
        public string Decision;          // null = sin decidir aun
        public string Porque;
        public bool Arranque;            // la decision vino de ver arrancar un juego (se puede memorizar su portada)
        public double MejorDistNorm = 999;  // (log) lo mas parecido a lo de antes visto
        public double Observado { get { return observado; } }

        public VueltaOArranque(string enMarcha, string candidato, List<double[]> imagenesAntes, DateTime t)
        {
            EnMarcha = enMarcha; Candidato = candidato; inicio = t;
            int oscuras = 0;
            if (imagenesAntes != null)
                foreach (double[] f in imagenesAntes)
                    if (f != null) { antes.Add(f); if (Oscura(f)) oscuras++; }
            antesOscuro = antes.Count > 0 && oscuras * 2 >= antes.Count;
        }

        public bool HayAntes { get { return antes.Count > 0; } }
        public bool AntesOscuro { get { return antesOscuro; } }

        static bool Oscura(double[] f)
        {
            double m = 0;
            foreach (double v in f) m += v;
            m /= f.Length;
            return m < 40 || PortadasConsola.NegroLiso(f);
        }

        // Lo mas parecido a las imagenes de antes del menu: (normalizada, en crudo)
        public void DistAntes(double[] f, out double norm, out double cruda)
        {
            norm = cruda = 999;
            foreach (double[] a in antes)
            {
                double dn = PortadasConsola.DistNorm(f, a), dc = PortadasConsola.Dist(f, a);
                if (dn < norm) norm = dn;
                if (dc < cruda) cruda = dc;
            }
        }

        // ¿Esa imagen es la de antes del menu? (clara o con dibujo; un negro no se parece a nada)
        public bool ComoAntes(double[] f)
        {
            if (f == null || antes.Count == 0 || PortadasConsola.NegroLiso(f)) return false;
            foreach (double[] a in antes)
                if (PortadasConsola.DistNorm(f, a) < MismaNorm && PortadasConsola.Dist(f, a) < MismaCruda) return true;
            return false;
        }

        void Decide(string quien, bool arranque, string porque)
        {
            if (Decision != null) return;
            Decision = quien; Arranque = arranque; Porque = porque;
        }

        // Una captura fuera del menu. f = PortadasConsola.Firma (null sin imagen); sinSenal; tenue = LecturaMenu.Tenue (oscura); portada = el
        // perfil de una portada CONOCIDA en esta captura y la anterior (o null); pantallaConsola = texto de una pantalla de la consola.
        // Devuelve la decision (null = aun no)
        public string Observa(DateTime t, double[] f, bool sinSenal, bool tenue, string portada, bool pantallaConsola)
        {
            if (Decision != null) return Decision;
            if (ultima != DateTime.MinValue)
            {
                double gap = (t - ultima).TotalSeconds;
                if (gap > 0 && gap <= HuecoMaxS) observado += gap;
            }
            ultima = t;
            if (portada != null)
            {
                if (portada == Candidato || GameProfiles.SameGame(portada, Candidato)) Decide(Candidato, true, "su portada en pantalla");
                else if (EnMarcha != null && (portada == EnMarcha || GameProfiles.SameGame(portada, EnMarcha))) Decide(EnMarcha, true, "la portada del que estaba en marcha (se ha vuelto a arrancar)");
                else Decide(portada, true, "la portada de otro juego conocido");
                return Decision;
            }
            if (sinSenal)
            {
                negroDesde = DateTime.MinValue; quietaPrev = null; oscuroDesde = DateTime.MinValue;
                if (sinSenalDesde == DateTime.MinValue) sinSenalDesde = t;
                double s = (t - sinSenalDesde).TotalSeconds;
                if (s >= SinSenalS) Decide(Candidato, true, "sin señal " + s.ToString("0.0", CultureInfo.InvariantCulture) + " s (la consola cambia de modo de video al arrancar otro juego)");
                else return Vence(t);
                return Decision;
            }
            sinSenalDesde = DateTime.MinValue;
            if (f == null) return Vence(t);
            // La de volver: la misma imagen que antes del menu
            double dn, dc;
            DistAntes(f, out dn, out dc);
            if (dn < MejorDistNorm && !PortadasConsola.NegroLiso(f)) MejorDistNorm = dn;
            if (ComoAntes(f))
            {
                Decide(EnMarcha, false, "la misma imagen que antes del menu (distancia " + dn.ToString("0.0", CultureInfo.InvariantCulture) + " sin el brillo, " +
                       dc.ToString("0.0", CultureInfo.InvariantCulture) + " en crudo)");
                return Decision;
            }
            // Negro liso seguido (no si lo de antes del menu era oscuro: podia ser una carga del mismo juego)
            if (PortadasConsola.NegroLiso(f) && !antesOscuro)
            {
                if (negroDesde == DateTime.MinValue) negroDesde = t;
                double s = (t - negroDesde).TotalSeconds;
                if (s >= NegroS) { Decide(Candidato, true, "negro liso " + s.ToString("0.0", CultureInfo.InvariantCulture) + " s (la consola cierra un juego y abre otro)"); return Decision; }
            }
            else negroDesde = DateTime.MinValue;
            // Una imagen quieta con dibujo que no es la de antes: la portada nueva del foco (o el menu de pausa del de antes: aun no decide)
            if (!pantallaConsola && PortadasConsola.ConDibujo(f) && dn >= DistintaNorm)
            {
                if (quietaPrev != null && PortadasConsola.Dist(quietaPrev, f) <= PortadasConsola.Quieta)
                {
                    if ((t - quietaDesde).TotalSeconds >= 1.2 && PortadaVista == null) PortadaVista = f;
                }
                else { quietaPrev = f; quietaDesde = t; }
            }
            else { quietaPrev = null; quietaDesde = DateTime.MinValue; }
            // Tras esa portada, su carga a oscuras (otra imagen, oscura y que no es la de antes) seguida: se arranca el del foco
            if (PortadaVista != null && !antesOscuro && tenue && PortadasConsola.Dist(f, PortadaVista) > 20 && dn >= DistintaNorm)
            {
                if (oscuroDesde == DateTime.MinValue) oscuroDesde = t;
                double s = (t - oscuroDesde).TotalSeconds;
                if (s >= OscuroTrasPortadaS) { Decide(Candidato, true, "una portada quieta nueva y luego su carga a oscuras " + s.ToString("0.0", CultureInfo.InvariantCulture) + " s"); return Decision; }
            }
            else if (!tenue) oscuroDesde = DateTime.MinValue;
            return Vence(t);
        }

        // Sin pruebas: el de antes, pasados ObservarS mirados o RelojMaxS de reloj
        public string Vence(DateTime t)
        {
            if (Decision != null) return Decision;
            if (observado >= ObservarS || (t - inicio).TotalSeconds >= RelojMaxS)
                Decide(EnMarcha, false, "sin pantalla de arranque en " + observado.ToString("0", CultureInfo.InvariantCulture) + " s mirados (" +
                       (t - inicio).TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s de reloj)" +
                       (MejorDistNorm < 900 ? ", lo mas parecido a antes del menu a " + MejorDistNorm.ToString("0.0", CultureInfo.InvariantCulture) : ""));
            return Decision;
        }
    }

    // 1.2.2: los perfiles con el nombre con basura del OCR (MigraNombresConsola, al abrir la app)
    static partial class GameProfiles
    {
        public static List<string> Nombres()
        {
            lock (sync) { Load(); List<string> r = new List<string>(); foreach (GameProfile p in list) r.Add(p.Name); return r; }
        }

        // (la prueba: FusionaNombre sobre una COPIA de profiles.txt en otra carpeta; devuelve las lineas que quedan)
        public static List<string> PruebaFusiona(string dir, Dictionary<string, string> m, string ultimo, List<string> info)
        {
            lock (sync)
            {
                string viejoDir = Dir;
                try
                {
                    Dir = dir; list = null; backedUp = false;
                    foreach (KeyValuePair<string, string> kv in m) { string i = FusionaNombre(kv.Key, kv.Value, ultimo); if (i != null) info.Add(kv.Key + " -> " + kv.Value + ": " + i); }
                    return new List<string>(File.ReadAllLines(Path.Combine(dir, "profiles.txt"), Encoding.UTF8));
                }
                finally { Dir = viejoDir; list = null; backedUp = false; }
            }
        }

        // El perfil "viejo" (nombre con basura) pasa a llamarse "nuevo". Si ya hay uno "nuevo", queda uno: con los ajustes del que diga
        // last_game.txt (ultimo) o, si no, del de mas abajo en profiles.txt (el mas nuevo); los iconos del HOME de los dos. null = sin perfil viejo
        public static string FusionaNombre(string viejo, string nuevo, string ultimo)
        {
            if (string.IsNullOrEmpty(viejo) || string.IsNullOrEmpty(nuevo) || viejo == nuevo) return null;
            lock (sync)
            {
                Load();
                GameProfile pv = null, pn = null;
                int iv = -1, inn = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Name == viejo) { pv = list[i]; iv = i; }
                    else if (list[i].Name == nuevo) { pn = list[i]; inn = i; }
                }
                if (pv == null) return null;
                Backup();
                if (pn == null) { pv.Name = nuevo; SaveAll(); return "perfil renombrado, con sus ajustes: " + pv.Summary(); }
                bool quedaViejo = ultimo == viejo || (ultimo != nuevo && iv > inn);
                GameProfile queda = quedaViejo ? pv : pn, va = quedaViejo ? pn : pv;
                foreach (double[] ic in va.Icons) queda.AddIcon(ic);
                queda.NameSure = pv.NameSure || pn.NameSure;
                list.Remove(va);
                queda.Name = nuevo;
                SaveAll();
                return "ya habia uno con el nombre bueno: queda uno, con los ajustes " + (quedaViejo ? "del de \"" + viejo + "\" (" : "de \"" + nuevo + "\" (") +
                       (ultimo == queda.Name || ultimo == viejo ? "el ultimo jugado" : "el mas nuevo") + "): " + queda.Summary();
            }
        }
    }

    // ===================================================================
    //  En la app (con src\MenuPs5.cs)
    // ===================================================================
    partial class TrayApp
    {
        // Las ultimas imagenes del juego (capturas fuera del menu clasificadas "Juego"), para saber al volver si es el mismo
        readonly List<KeyValuePair<DateTime, double[]>> ps5Recientes = new List<KeyValuePair<DateTime, double[]>>();
        List<double[]> ps5AntesMenu;                 // las de antes de abrir el menu (sin las de 1 s antes de verlo: el menu apareciendo)
        // Las capturas mientras se confirmaba la salida del menu (aun en el, sin el menu a la vista): las pruebas de enseguida
        sealed class CapturaSalida { public DateTime At; public double[] Firma; public bool SinSenal, Tenue, Consola; }
        readonly List<CapturaSalida> ps5CapturasSalida = new List<CapturaSalida>();
        VueltaOArranque ps5Vuelta;                   // la comprobacion en curso (con ps5Candidato)
        GameProfile ps5VueltaCopia;                  // los ajustes del de antes al salir (si al final era el otro, se le devuelven)
        bool ps5ArranqueProbable;                    // la salida fue a un juego arrancado (no la vuelta al que estaba en marcha): portada nueva si

        // Magpie (re)abriendose (passesBusy: la entrada en un juego, ajustes aplicados) con la imagen de mpv ya cambiada: se sigue capturando,
        // solo para el menu de la consola y la comprobacion de la salida (AnalyzeGrab -> MenuPs5Captura). Antes no se miraba nada esos ~10 s
        bool CapturaPreparando
        {
            get
            {
                ConsolaPerfil c = Consolas.Actual;
                return passesBusy && !lsReady && !InHome && !homeSwitching && relaunchSize == null && !relaunchEngine && c.MenuPropio && !c.TieneHome &&
                       JuegoConsola.Activo(c, cfg) && DateTime.Now >= switchUntil;
            }
        }

        // (GrabTick) cada cuanto mientras se comprueba la salida: 0,4 s (el negro de arrancar otro juego se ve en 1,2 s)
        bool VueltaPendiente { get { VueltaOArranque v = ps5Vuelta; return v != null && v.Decision == null; } }

        // Una captura del juego (fuera del menu, "Juego"): a las recientes (las 6 ultimas, de los ultimos 20 s)
        void VueltaRecuerda(DateTime now, double[] f)
        {
            if (f == null || VueltaPendiente) return;
            lock (ps5Recientes)
            {
                ps5Recientes.Add(new KeyValuePair<DateTime, double[]>(now, f));
                while (ps5Recientes.Count > 6 || (ps5Recientes.Count > 0 && (now - ps5Recientes[0].Key).TotalSeconds > 20)) ps5Recientes.RemoveAt(0);
            }
        }

        // Las imagenes del juego de antes de ver el menu (las 3 ultimas; las de 1 s antes de la primera captura con el menu no: el menu
        // apareciendo, sin la hora aun)
        List<double[]> VueltaAntesDe(DateTime primeraMenu)
        {
            List<double[]> l = new List<double[]>();
            DateTime lim = (primeraMenu == DateTime.MinValue ? DateTime.Now : primeraMenu).AddSeconds(-1.0);
            lock (ps5Recientes)
            {
                foreach (KeyValuePair<DateTime, double[]> kv in ps5Recientes) if (kv.Key <= lim) l.Add(kv.Value);
                ps5Recientes.Clear();
            }
            if (l.Count > 3) l.RemoveRange(0, l.Count - 3);
            return l;
        }

        // Al entrar en el menu
        void VueltaAlEntrarMenu(DateTime primeraMenu)
        {
            ps5AntesMenu = VueltaAntesDe(primeraMenu);
            lock (ps5CapturasSalida) ps5CapturasSalida.Clear();
            VueltaOArranque v = ps5Vuelta;
            if (v != null && v.Decision == null)
                Log.Write("Comprobacion de la salida anterior sin decidir (\"" + v.EnMarcha + "\" o \"" + v.Candidato + "\"): de vuelta en el menu, el juego en foco al abrirlo dira cual esta en marcha");
            ps5Vuelta = null;
            ps5Candidato = null;
        }

        // En el menu, una captura sin el (confirmando la salida): para las pruebas de enseguida. Con el menu a la vista, se olvidan
        void VueltaCapturaEnMenu(DateTime now, double[] f, LecturaMenu l, bool noSignal)
        {
            lock (ps5CapturasSalida)
            {
                if (!noSignal && l != null && (l.Vista == VistaMenu.Menu || l.Vista == VistaMenu.MenuTexto)) { ps5CapturasSalida.Clear(); return; }
                CapturaSalida c = new CapturaSalida();
                c.At = now; c.Firma = f; c.SinSenal = noSignal; c.Tenue = l != null && l.Tenue; c.Consola = false;
                ps5CapturasSalida.Add(c);
                while (ps5CapturasSalida.Count > 24) ps5CapturasSalida.RemoveAt(0);
            }
        }

        // MenuPs5Salir: se jugaba enMarcha y el icono en foco es cand. Empieza la comprobacion con las capturas que sacaron del menu (pruebas
        // de enseguida). Devuelve la decision ya tomada (o null: pendiente, se sigue con enMarcha hasta ver la prueba)
        string VueltaEmpieza(string enMarcha, string cand, DateTime now, out string porque)
        {
            List<CapturaSalida> cs;
            lock (ps5CapturasSalida) { cs = new List<CapturaSalida>(ps5CapturasSalida); ps5CapturasSalida.Clear(); }
            DateTime t0 = cs.Count > 0 && cs[0].At < now ? cs[0].At : now;
            VueltaOArranque v = new VueltaOArranque(enMarcha, cand, ps5AntesMenu, t0);
            string prev = null;
            foreach (CapturaSalida c in cs)
            {
                // (la portada conocida: dos capturas seguidas con la misma, como PortadaRapida)
                string p = null;
                if (c.Firma != null && PortadasConsola.ConDibujo(c.Firma))
                {
                    double d;
                    string q = PortadasConsola.Busca(c.Firma, out d);
                    if (q != null && d < PortadaSegura) { if (prev == q) p = q; prev = q; } else prev = null;
                }
                if (v.Observa(c.At, c.Firma, c.SinSenal, c.Tenue, p, c.Consola) != null) break;
            }
            string antes = (v.HayAntes ? "" : "sin imagen de antes del menu; ") + (v.AntesOscuro ? "la de antes del menu era oscura; " : "");
            if (v.Decision != null)
            {
                porque = antes + v.Porque + " (con las " + cs.Count + " capturas que sacaron del menu)";
                return v.Decision;
            }
            // (pendiente: los ajustes del de antes, por si al final era el otro y se cambian entre medias). Se publica lo ultimo: desde aqui
            // la alimenta el hilo de las capturas (MenuPs5Captura)
            GameProfile gp = GameProfiles.Find(enMarcha);
            ps5VueltaCopia = gp != null && gp.Name == enMarcha ? CopiaPerfil(gp) : null;
            porque = antes + cs.Count + " capturas al salir sin prueba aun";
            ps5Candidato = cand;
            ps5Vuelta = v;
            return null;
        }

        // El juego en marcha resulto ser otro (la comprobacion, o el juego en foco al volver al menu): lo que se guardo mientras tanto en el
        // perfil de "nombre" (el mando, el menu de ajustes) era del otro: sus ajustes vuelven a los de la salida
        void VueltaDevuelveAjustes(string nombre)
        {
            GameProfile copia = ps5VueltaCopia;
            ps5VueltaCopia = null;
            if (copia == null || nombre == null || copia.Name != nombre) return;
            GameProfile gp = GameProfiles.Find(copia.Name);
            if (gp == null || gp.Name != copia.Name || MismosAjustes(gp, copia)) return;
            GameProfiles.SaveSettings(gp, copia);
            Log.Write("Ajustes de \"" + copia.Name + "\": vuelven a los de antes de la salida del menu (lo cambiado entre medias era del otro juego): " + copia.Summary());
        }

        static GameProfile CopiaPerfil(GameProfile p)
        {
            GameProfile c = new GameProfile();
            c.Name = p.Name; c.NameSure = p.NameSure; c.Capture = p.Capture; c.Level = p.Level; c.Passes = p.Passes; c.Dlss5 = p.Dlss5; c.Scaler = p.Scaler;
            c.ScalerPick = p.ScalerPick; c.Cap = p.Cap; c.Mode120 = p.Mode120; c.Fg = p.Fg; c.Pick60 = p.Pick60; c.Pick30 = p.Pick30;
            return c;
        }

        static bool MismosAjustes(GameProfile a, GameProfile b)
        {
            return a.Capture == b.Capture && a.Level == b.Level && a.Passes == b.Passes && a.Dlss5 == b.Dlss5 && a.Scaler == b.Scaler && a.ScalerPick == b.ScalerPick &&
                   a.Cap == b.Cap && a.Mode120 == b.Mode120 && a.Fg == b.Fg && a.Pick60 == b.Pick60 && a.Pick30 == b.Pick30;
        }

        // Una captura fuera del menu con la comprobacion pendiente (MenuPs5Captura, tambien mientras Magpie prepara). portada = conocida en
        // esta y la anterior (PortadaRapida) o null
        void VueltaObserva(DateTime now, double[] f, bool noSignal, bool tenue, string portada)
        {
            VueltaOArranque v = ps5Vuelta;
            if (v == null || v.Decision != null) return;
            string d = v.Observa(now, f, noSignal, tenue, portada, false);
            if (d == null) return;
            VueltaDecidida(v);
        }

        void VueltaDecidida(VueltaOArranque v)
        {
            if (ps5Vuelta != v) return;
            ps5Vuelta = null;
            ps5Candidato = null;
            string cur;
            lock (profileSync) cur = curGame;
            if (v.Decision == v.EnMarcha || (cur != null && (v.Decision == cur || GameProfiles.SameGame(v.Decision, cur))))
            {
                Log.Write("Vuelta al juego en marcha: " + v.Porque + "; sigue \"" + (cur ?? v.EnMarcha) + "\" (el icono en foco era \"" + v.Candidato + "\", no se arranco)");
                // (lo de despues ya es este juego: sus imagenes valen para la proxima vuelta)
                return;
            }
            // Era el otro: los ajustes que el de antes cogiera mientras tanto (el mando, el menu de ajustes) no son suyos
            VueltaDevuelveAjustes(v.EnMarcha);
            ps5ArranqueProbable = v.Arranque;
            // (lo quieto de despues ya no se apunta como portada: el juego aun es el de antes hasta que MenuPs5Tick ponga este; la portada
            // vista al arrancarlo, abajo)
            lock (homeSync) { ps5PortadaHasta = DateTime.MinValue; ps5PortadaPrev = null; }
            PasaACandidato(v.Decision, v.Porque + " (" + v.Observado.ToString("0.0", CultureInfo.InvariantCulture) + " s mirados)");
            // La portada quieta vista al arrancarlo: la suya (el juego ya es seguro: el del icono en foco y su arranque visto)
            double[] pv = v.PortadaVista;
            if (pv != null && v.Arranque && JuegoConsola.TagConsola(v.Decision) != null)
            {
                double dist;
                string ya = PortadasConsola.Busca(pv, out dist);
                if (ya == null)
                {
                    PortadasConsola.Guarda(v.Decision, pv);
                    Log.Write("Portada del juego: nueva, memorizada para \"" + v.Decision + "\" (vista al arrancarlo)");
                }
            }
        }

        // La Bienvenida / elegir usuario (al encender o despertar la consola): no es el menu de verdad, y hasta verlo la salida es prudente
        // (solo una imagen de juego clara y seguida saca: las particulas oscuras de despues no). Cambia l.Vista si hace falta
        readonly EstadoBienvenida ps5Bienvenida = new EstadoBienvenida();
        void BienvenidaObserva(LecturaMenu l, DateTime now)
        {
            string log = ps5Bienvenida.Observa(l, now);
            if (log != null) Log.Write(log);
        }

        // La salida prudente: tras el menu de apagado o tras la Bienvenida (hasta el menu de verdad)
        DateTime PrudenteHastaAhora()
        {
            DateTime a = PrudenteTrasApagado(), b = ps5Bienvenida.Hasta;
            return a > b ? a : b;
        }

        // ---------------- 1.2.2: al abrir la app, los nombres con basura pegada a su nombre bueno ----------------
        // (perfiles, iconos y portadas de la consola y el ultimo juego). Dos perfiles del mismo juego: queda uno, con los ajustes del que
        // diga last_game.txt o, si no, del mas nuevo (el de mas abajo en profiles.txt); los iconos y portadas, de los dos
        void MigraNombresConsola()
        {
            try
            {
                List<string> nombres = new List<string>(GameProfiles.Nombres());
                nombres.AddRange(IconosConsola.Nombres());
                nombres.AddRange(PortadasConsola.Nombres());
                string ultimo = LoadText(Paths.LastGameFile);
                if (!string.IsNullOrEmpty(ultimo)) nombres.Add(ultimo);
                Dictionary<string, string> m = NombresConsola.Migracion(nombres);
                if (m.Count == 0) return;
                foreach (KeyValuePair<string, string> kv in m)
                {
                    string info = GameProfiles.FusionaNombre(kv.Key, kv.Value, ultimo);
                    IconosConsola.Renombra(kv.Key, kv.Value);
                    PortadasConsola.Renombra(kv.Key, kv.Value);
                    if (ultimo == kv.Key) { SaveText(Paths.LastGameFile, kv.Value); ultimo = kv.Value; }
                    lock (profileSync) { if (curGame == kv.Key) curGame = kv.Value; }
                    Log.Write("Nombre de juego con basura del OCR: \"" + kv.Key + "\" -> \"" + kv.Value + "\"" + (info != null ? " (" + info + ")" : " (iconos y portadas)"));
                }
            }
            catch (Exception ex) { Log.Error("Limpiando los nombres de los juegos de la consola", ex); }
        }
    }
}
