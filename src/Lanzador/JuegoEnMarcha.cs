// JuegoEnMarcha.cs - 22-11 (1.2.3, agente JUEGO): que juego esta en marcha cuando la app no lo ha visto arrancar. El usuario: "me he
// metido a la app con el gta 6 ya abierto por ejemplo y la app no sabia que juego era, si puedes arreglalo y si va a quedar perfecto si
// no no".
//
// Lo que paso (log del 22-11, PS5, app 1.2.2):
//  - 10:25:46 la app se abrio con GTA VI ya en marcha (sus pantallas de carga): salida prudente a los 5 s al perfil de toda la consola
//    ("Consola"). Bien: no hay como saber el juego sin el menu.
//  - 10:26:31-34 primera visita al menu (unos 3 s): NINGUNA lectura de la etiqueta del juego en foco y al salir otra vez "Consola". Las dos
//    capturas que vieron el menu (31,9 y 32,3) eran del inicio apareciendo (la copia de la pantalla del telon congelado no se parecia a la
//    imagen de mpv: diferencia 26, la pantalla moviendose; en las visitas quietas 5-8); luego la app paso al menu a 4K sin pedir ni una
//    captura 1,4 s (el cambio: passesBusy / lsReady=false y el margen de switchUntil de 1 s tras cambiar mpv) y la siguiente ya era la
//    vuelta al juego. Las lecturas de antes de entrar se borraban al entrar (MenuPs5Entrar) y el primer foco (1.2.05) solo valia en 1,5 s y
//    solo si la app creia estar en otro juego con nombre (no con "Consola").
//  - 10:26:58 segunda visita: la etiqueta leida en la primera captura -> GTA VI. Y 10:27:17 se guardo como "portada" de GTA VI una imagen
//    quieta del juego: al salir de "Consola" se tomaba por un arranque.
// Ahora (FocoVisita y los enganches de MenuPs5.cs / GrabTick):
//  - El PRIMER juego en foco al abrir el menu desde un juego es el que esta en marcha (la PS5 lo pone en foco): se busca hasta 5 s y vale
//    leido en 1,5 s (como 1.2.05) o mas tarde si el icono en foco es el de una captura del principio (el foco no se ha movido).
//  - Mientras no se sepa: capturas cada 0,25 s y TAMBIEN mientras la app pasa al menu (sin el hueco de 1,4 s), solo para leer el foco.
//  - Con la app sin saber el juego ("Consola"), el primer foco pasa a ser el juego en marcha: al salir del menu se aplica su perfil (o, si
//    el foco esta en otro, la comprobacion de 1.2.2: vuelta al de antes o arranque del otro). Volver a el no es un arranque: lo quieto de
//    despues no se guarda como su portada.
// C# 5
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace ConsoleEnhancer
{
    // ===================================================================
    //  El primer juego en foco de una visita al menu (pura: la prueba la usa sin la app)
    // ===================================================================
    sealed class FocoVisita
    {
        public const double TempranoS = 1.5;   // (1.2.05) leido en este tiempo desde la primera captura con el menu: el juego en marcha
        public const double PlazoS = 5.0;      // despues ya no se busca (y las capturas vuelven a su ritmo)

        public DateTime Desde = DateTime.MinValue;   // la primera captura con el menu de esta visita (desde un juego); con el inicio, VeInicio
        public string Nombre;                        // el primer foco (la etiqueta) o null
        public double[] Icono;                       // su icono (puede ser null)
        public DateTime At = DateTime.MinValue;
        public bool Cerrada;                         // ya no se decide (el foco ha podido moverse, o paso el plazo)
        public bool Aplicado;                        // (la app) ya se tomo como el juego en marcha
        public string Porque;                        // (log)
        readonly List<double[]> tempranos = new List<double[]>();   // iconos en foco del principio (sin la etiqueta legible aun)

        readonly object sync = new object();   // (la usan el hilo de las capturas y el de entrar / salir del menu)

        public bool InicioVisto;                     // ya se vio el inicio (sus pestañas): desde ahi cuenta el plazo

        public bool Activa { get { return Desde != DateTime.MinValue; } }
        public bool Sabida { get { return Nombre != null; } }

        public void Reinicia()
        {
            lock (sync)
            {
                Desde = DateTime.MinValue; Nombre = null; Icono = null; At = DateTime.MinValue; Cerrada = false; Aplicado = false; Porque = null;
                InicioVisto = false;
                tempranos.Clear();
            }
        }

        public void Empieza(DateTime t) { lock (sync) { if (Activa) return; Reinicia(); Desde = t; } }

        // La primera captura con el INICIO de la consola en esta visita (con la PS5 el boton PS puede abrir antes el centro de control, sin
        // la etiqueta del juego; al pasar al inicio el juego en marcha esta en foco): el plazo y los iconos del principio cuentan desde aqui
        public void VeInicio(DateTime t)
        {
            lock (sync)
            {
                if (!Activa || InicioVisto || Sabida) return;
                InicioVisto = true;
                Desde = t; Cerrada = false; Porque = null;
                tempranos.Clear();
            }
        }

        // ¿Aun se busca? (capturas mas seguidas y tambien mientras la app pasa al menu)
        public bool PorLeer(DateTime t)
        {
            lock (sync)
            {
                if (!Activa || Sabida || Cerrada) return false;
                double s = (t - Desde).TotalSeconds;
                return s >= -1 && s < PlazoS;
            }
        }

        // Una captura del menu de la visita. nombre = la etiqueta del juego en foco (fuerte) o null; icono = el icono en foco de esta captura
        // (JuegoConsola.IconoFoco, null si no hay o es un fundido). true = se acaba de saber el primer foco
        public bool Observa(DateTime t, string nombre, double[] icono) { lock (sync) return ObservaSinLock(t, nombre, icono); }

        bool ObservaSinLock(DateTime t, string nombre, double[] icono)
        {
            if (!Activa || Sabida || Cerrada) return false;
            double s = Math.Max(0, (t - Desde).TotalSeconds);
            if (s > PlazoS) { Cerrada = true; Porque = "sin la etiqueta en " + PlazoS.ToString("0", CultureInfo.InvariantCulture) + " s"; return false; }
            if (string.IsNullOrEmpty(nombre))
            {
                if (icono != null && s <= TempranoS) tempranos.Add(icono);
                return false;
            }
            string ss = s.ToString("0.0", CultureInfo.InvariantCulture);
            if (s <= TempranoS) { Pon(t, nombre, icono, "leido a los " + ss + " s de ver " + (InicioVisto ? "el inicio" : "el menu")); return true; }
            // Mas tarde (el inicio apareciendo, la app pasando al menu): solo con el icono en foco de una captura del principio
            double mejor = 999;
            if (icono != null) foreach (double[] e in tempranos) mejor = Math.Min(mejor, JuegoConsola.DistHuella(e, icono));
            if (mejor < JuegoConsola.MismoIcono)
            {
                Pon(t, nombre, icono, "leido a los " + ss + " s, con el mismo icono en foco que al ver " + (InicioVisto ? "el inicio" : "el menu") + " (distancia " +
                    mejor.ToString("0.0", CultureInfo.InvariantCulture) + ")");
                return true;
            }
            Cerrada = true;
            Porque = "la primera etiqueta leida a los " + ss + " s " + (icono == null ? "sin su icono" : tempranos.Count == 0 ? "sin iconos del principio con que compararla" :
                     "con otro icono en foco que al principio (" + mejor.ToString("0.0", CultureInfo.InvariantCulture) + ")") + ": el foco ha podido moverse";
            return false;
        }

        void Pon(DateTime t, string nombre, double[] icono, string porque) { Nombre = nombre; Icono = icono; At = t; Porque = porque; }
    }

    // ===================================================================
    //  En la app (enganches en MenuPs5.cs y en GrabLoop / GrabTick de Switch2Magpie.cs)
    // ===================================================================
    partial class TrayApp
    {
        readonly FocoVisita ps5Visita = new FocoVisita();

        // ¿Se esta buscando el primer foco de esta visita al menu? (GrabTick: capturas cada 0,25 s; MenuPs5Captura: la etiqueta en cada una)
        bool FocoMenuPorLeer
        {
            get { return ps5CiegoDesde == DateTime.MinValue && ps5Visita.PorLeer(DateTime.Now) && JuegoConsola.Activo(Consolas.Actual, cfg); }
        }

        // Pasando al menu (StartHomeSwitch hacia el menu: telon congelado, Magpie cerrandose, mpv cambiando) sin el primer foco aun: se sigue
        // capturando (solo para leer el foco, AnalyzeGrab -> MenuPs5Captura). No con mpv recargando la captura ni reabriendose
        bool CapturaEntrandoMenu
        {
            get
            {
                ConsolaPerfil c = Consolas.Actual;
                return homeBusy && homeSwitchToHome && !homeSwitching && relaunchSize == null && !relaunchEngine && c.MenuPropio && !c.TieneHome &&
                       FocoMenuPorLeer;
            }
        }

        // MenuPs5Captura, cada captura con el menu en una visita: el icono en foco (aunque la etiqueta no se lea) y la etiqueta leida
        // pestanas = esta captura se reconocio por las pestañas del inicio (DetectorMenu: l.Hora "pestañas")
        void FocoVisitaObserva(Bitmap full, DateTime now, ConsolaPerfil c, string nombreFuerte, double[] icFoco, bool pestanas)
        {
            if (ps5CiegoDesde != DateTime.MinValue || !ps5Visita.Activa || ps5Visita.Sabida) return;
            if (!ps5Visita.InicioVisto)
            {
                // (la PS5: la etiqueta del juego en foco solo sale en el inicio; sin ella, sus pestañas -OCR de su zona, ~20 ms- dicen si ya
                // se ve el inicio o aun el centro de control. La Xbox: desde la primera captura de su menu)
                bool inicio = c.Familia != "playstation" || !string.IsNullOrEmpty(nombreFuerte) || pestanas || JuegoConsola.EsInicio(full);
                if (!inicio) return;
                ps5Visita.VeInicio(now);
            }
            if (!ps5Visita.PorLeer(now)) return;
            double[] ic = icFoco;
            if (ic == null && (c.Familia == "playstation" || c.Familia == "xbox"))
            {
                ic = JuegoConsola.IconoFoco(full, c);
                if (ic != null && JuegoConsola.PareceFundido(ic)) ic = null;
            }
            if (ps5Visita.Observa(now, nombreFuerte, ic))
                Log.Write("Menu de la consola: el juego en foco al abrirlo (el que esta en marcha): \"" + ps5Visita.Nombre + "\" (" + ps5Visita.Porque + ")");
            else if (ps5Visita.Cerrada && ps5Visita.Porque != null && !string.IsNullOrEmpty(nombreFuerte))
                Log.Write("Menu de la consola: el juego en marcha no se sabe por el foco (" + ps5Visita.Porque + ")");
        }

        // (la prueba, --test-juego-en-marcha: cuando se captura para leer el primer foco, con el estado de la app puesto a mano; con la PS5 de
        // games\console.txt de la copia de datos)
        internal static string CapturasSelfTest(out int fallos)
        {
            StringBuilder sb = new StringBuilder();
            int f = 0;
            DateTime t0 = DateTime.Now;
            Func<TrayApp> jugando = delegate
            {
                TrayApp t = new TrayApp(new Config(), true);
                t.lsReady = true; t.cfg.HomeNative = false;
                return t;
            };
            Action<string, bool, bool> mira = delegate (string que, bool got, bool quiero)
            {
                bool ok = got == quiero;
                if (!ok) f++;
                sb.Append(ok ? "  OK    " : "  FALLO ").Append(que).Append(": ").Append(got ? "si" : "no").Append(ok ? "" : " (se esperaba " + (quiero ? "si" : "no") + ")").Append("\r\n");
            };
            sb.Append("  (consola: ").Append(Consolas.Actual.Corto).Append(")\r\n");
            TrayApp a = jugando();
            mira("jugando, sin visita: capturas rapidas para el foco", a.FocoMenuPorLeer, false);
            a.ps5Visita.Empieza(t0.AddSeconds(-0.4));
            mira("jugando, menu visto hace 0,4 s: se busca el foco", a.FocoMenuPorLeer, true);
            // pasando al menu (StartHomeSwitch hacia el: telon congelado, Magpie cerrandose)
            a.homeBusy = true; a.homeSwitchToHome = true; a.passesBusy = true; a.lsReady = false; a.cfg.HomeNative = true;
            mira("pasando al menu sin el foco: se captura", a.CapturaEntrandoMenu, true);
            a.homeSwitching = true;
            mira("pasando al menu con mpv recargando la captura: no", a.CapturaEntrandoMenu, false);
            a.homeSwitching = false; a.relaunchEngine = true;
            mira("pasando al menu reabriendo el motor: no", a.CapturaEntrandoMenu, false);
            a.relaunchEngine = false; a.relaunchSize = "1920x1080";
            mira("pasando al menu cambiando la captura: no", a.CapturaEntrandoMenu, false);
            a.relaunchSize = null;
            a.ps5CiegoDesde = t0;
            mira("visita ciega (la app ocupada al ver el menu): no", a.CapturaEntrandoMenu, false);
            a.ps5CiegoDesde = DateTime.MinValue;
            a.homeSwitchToHome = false;
            mira("del menu a un juego: no", a.CapturaEntrandoMenu, false);
            a.homeSwitchToHome = true;
            a.ps5Visita.Observa(t0, "Grand Theft Auto VI", null);
            mira("con el foco ya leido: no", a.CapturaEntrandoMenu, false);
            // ya en el menu
            TrayApp b = jugando();
            b.cfg.HomeNative = true;
            b.ps5Visita.Empieza(t0.AddSeconds(-2));
            mira("en el menu a los 2 s sin el foco: se busca", b.FocoMenuPorLeer, true);
            b.ps5Visita.Reinicia(); b.ps5Visita.Empieza(t0.AddSeconds(-5.5));
            mira("en el menu a los 5,5 s: ya no", b.FocoMenuPorLeer, false);
            TrayApp c = jugando();
            c.cfg.Ps5RecognizeGames = false;
            c.ps5Visita.Empieza(t0);
            mira("ps5_reconocer_juegos=0: nada", c.FocoMenuPorLeer, false);
            fallos = f;
            return sb.ToString();
        }

        // 1.2.3: el modo guardado (games\mode.txt: 30 / 60 / 120) en un perfil nuevo de la consola (AplicarPerfilConsola, al abrir la app va
        // antes de que Worker lea el fichero). true = el perfil cambia
        static bool ModoGuardado(GameProfile p)
        {
            string m = LoadText(Paths.ModeFile);
            if (p == null || (m != "30" && m != "60" && m != "120")) return false;
            bool cap = m == "30", m120 = m == "120";
            if (p.Cap == cap && p.Mode120 == m120) return false;
            p.Cap = cap;
            p.Mode120 = m120;
            return true;
        }

        // El perfil de un juego en foco (icono conocido o, si no, su nombre); null si no vale
        string PerfilDeFoco(ConsolaPerfil c, string nombre, double[] icono)
        {
            if (nombre == null) return null;
            double d;
            string p = icono != null ? IconosConsola.Busca(icono, out d) : null;
            if (p != null) return p;
            string nom = JuegoConsola.NombrePerfil(c, nombre);
            if (NombreProvisional(nom)) return null;
            GameProfile gp = GameProfiles.Find(nom);
            return gp != null ? gp.Name : nom;
        }

        // Al entrar en el menu y al salir de el: el primer foco de la visita es el juego en marcha. Si la app no lo sabia (el perfil de la
        // consola: abierta con el juego ya en marcha) o creia que era otro (1.2.05: se arranco sin que lo viera), manda el. Una vez por visita
        void PrimerFocoComoEnMarcha(ConsolaPerfil c, string cuando)
        {
            if (!ps5Visita.Sabida || ps5Visita.Aplicado || !RecoJuegosActivo(c)) return;
            ps5Visita.Aplicado = true;
            string pf = PerfilDeFoco(c, ps5Visita.Nombre, ps5Visita.Icono);
            if (pf == null || JuegoConsola.EsAppSistema(pf)) return;
            string antes = ps5JuegoAntesMenu;
            bool antesJuego = antes != null && JuegoConsola.TagConsola(antes) != null;
            if (antesJuego && (antes == pf || GameProfiles.SameGame(antes, pf))) return;
            ps5JuegoAntesMenu = pf;
            if (antesJuego)
            {
                Log.Write("Menu de la consola (" + cuando + "): el juego en foco al abrirlo (el que esta en marcha) es \"" + pf + "\", no \"" + antes +
                          "\" (se arranco sin que la app lo viera): se toma ese como el juego de antes del menu");
                VueltaDevuelveAjustes(antes);   // (1.2.2: lo guardado en el de antes mientras tanto era de este)
            }
            else
                Log.Write("Menu de la consola (" + cuando + "): la app no sabia que juego estaba en marcha (\"" + (antes ?? "ninguno") + "\"); el juego en foco al " +
                          "abrir el menu es \"" + pf + "\": ese es el juego en marcha");
        }
    }
}
