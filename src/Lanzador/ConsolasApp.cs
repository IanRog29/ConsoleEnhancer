// ConsolasApp.cs - Consolas.cs dentro de la app (18-11, agente CONSOLAS, fase 2): la consola al abrir, el perfil de toda la consola cuando
// su menu no se reconoce (todas menos la Switch 2), la captura que tiene sentido, la espera de "No Signal" de cada una, el aviso de
// HDCP (PlayStation), los juegos a 40, la deteccion con consola=auto (el HOME de la Switch 2 visto de reojo y las palabras de la
// pantalla) y el cambio de consola en marcha. Los enganches en Switch2Magpie.cs son de una linea (ver fase2.md). C# 5
using System;
using System.Drawing;
using System.Globalization;
using System.Threading;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        bool homeIni = true;                         // home_4k_nativo del ini, antes de mirar la consola
        int obsHomeHits;                             // capturas seguidas con las franjas del HOME de la Switch 2 (consola sin HOME reconocido)
        DateTime obsTextAt = DateTime.MinValue;      // la ultima lectura de la pantalla para la deteccion (OCR)
        DateTime obsStart = DateTime.MinValue;       // desde cuando se mira (los primeros minutos, mas a menudo)
        volatile string audioNow;                    // lo ultimo del medidor de audio de la consola: "sound" / "silent" / null
        DateTime darkSince = DateTime.MinValue;      // PlayStation: negro y en silencio desde (el HDCP)
        bool hdcpSaid;
        // 20-11 (HDCP120): negro DE VERDAD para el aviso de HDCP (lit por debajo de esto = casi 0 % con luz). Mucho mas estricto que
        // HomeLabel.DarkLit (0,03, que cuenta tambien una pantalla de carga oscura): el HDCP da negro liso, una carga o una escena oscura no
        const double HdcpDarkLit = 0.004;
        DateTime fps40Since = DateTime.MinValue;     // un juego a 40 desde
        bool fps40Said;
        string waitHint = "Pulsa Esc para cancelar"; // WaitForSignal: el texto pequeño de "Enciende la consola" puesto

        // Worker: tras "homeRef = LoadRef();" y antes de "cfg.HomeNative = cfg.HomeNativeEnabled;". La consola (consola= del ini o la ultima
        // vista), y si su menu no se reconoce, sin HOME a 4K nativo y con los ajustes guardados de toda la consola
        void ConsolaAlAbrir()
        {
            MigraNombresConsola();   // (1.2.2: nombres de juegos con basura del OCR a su nombre bueno, src\VueltaJuego.cs)
            Switch2AlAbrir();        // (23-11, SWITCH2: los juegos de la Switch 2 de la version de antes y los "Juego N" con nombre; src\Lanzador\Switch2Datos.cs)
            homeIni = cfg.HomeNativeEnabled;
            ThreadPool.QueueUserWorkItem(delegate { SteamAcceso.AlAbrirApp(); });   // (1.3.3, VARIOS: el acceso de Steam a esta carpeta, src\Lanzador\SteamAcceso.cs)
            Log.Write(Consolas.Iniciar());
            Consolas.Cambiada -= OnConsolaCambiada;
            Consolas.Cambiada += OnConsolaCambiada;
            obsStart = DateTime.Now;
            ConsolaPerfil c = Consolas.Actual;
            // (1.3.1, SWITCH2: los ajustes guardados en games\*.txt son de la ultima consola vista: src\Lanzador\AjustesDeConsola.cs)
            AjustesSonDe(c);
            // (19-11: la PS5 tambien, con su menu propio: src\MenuPs5.cs; se empieza en su menu a 4K nativo, como en el HOME)
            // 20-11 (CE-PERFILES): las consolas con menu propio (PS5, Xbox) necesitan su menu a 4K nativo para reconocer cada juego en el
            // centro de control / conmutador y guardar y re-aplicar su perfil; eso NO depende de home_4k_nativo (clave pensada para el HOME de
            // la Switch 2, que si se puede querer escalado). Un ini viejo con home_4k_nativo=0 (lo escribia el plan para todo lo que no fuera
            // Switch 2) ya no apaga el reconocimiento por juego de la PS5: su menu propio siempre sale a 4K nativo
            cfg.HomeNativeEnabled = (homeIni && c.TieneHome) || c.MenuPropio;
            if (!c.TieneHome) AplicarPerfilConsola(c);
            else if (!c.CapturaUtil(cfg.VideoSize)) cfg.VideoSize = c.CapturaCercana(cfg.VideoSize);
        }

        // Los ajustes guardados de la consola ("Consola" en games\profiles.txt; nuevo con los de ahora), antes de abrir la imagen:
        // como LeaveHome pero sin telon ni cambio de captura (aun no hay nada abierto)
        void AplicarPerfilConsola(ConsolaPerfil c)
        {
            GameProfile p = GameProfiles.Find(c.PerfilConsola);
            if (p != null && p.Name != c.PerfilConsola) p = null; // (solo ese nombre: Find tambien casa nombres parecidos de juegos)
            bool nuevo = p == null;
            string deOtra = null;
            if (nuevo)
            {
                // (1.3.1, SWITCH2: con los ajustes de ahora de otra consola -la Switch 2 antes de la PS5-, los de partida de esta, no esos)
                p = PerfilDePartida(c.PerfilConsola, out deOtra);
                // 1.2.3 (JUEGO; lo vio MULTI en la 1.2.2): al abrir la app esto va antes de leer games\mode.txt (Worker): el perfil nuevo salia
                // con el modo 60 de partida y, abajo, pisaba el modo guardado. El modo guardado manda (1.3.1: si es de esta consola)
                if (deOtra == null && ModoGuardado(p)) Log.Write("Consola " + c.Corto + ": perfil \"" + p.Name + "\" nuevo con el modo guardado (" + p.ModoFps + ")");
                p.NameSure = true;
                p.Capture = c.CapturaCercana(p.Capture);
                GameProfiles.Save(p);
            }
            lock (profileSync)
            {
                curGame = p.Name;
                curGameGuess = false;
                dlss5Level = p.Level;
                SaveText(Paths.Dlss5LevelFile, p.Level.ToString(CultureInfo.InvariantCulture));
                dlss5Passes = p.Passes;
                SaveText(Paths.Dlss5PassesFile, p.Passes.ToString(CultureInfo.InvariantCulture));
                bool pm120 = p.Mode120 && Puede120(c.CapturaCercana(p.Capture)); // 20-11 (HZ120): 120 solo si la captura/consola/tele lo admiten
                cfg.CapMode = p.Cap && !pm120;
                cfg.Mode120 = pm120;
                cfg.Mode40 = p.Mode40 && cfg.CapMode;   // (1.3.2, MODO40)
                SaveText(Paths.ModeFile, cfg.ModoFps.ToString(CultureInfo.InvariantCulture));
                want30 = cfg.CapMode;
                slow30 = fast60 = 0;
                cfg.VideoSize = c.CapturaCercana(p.Capture);
                SaveText(Paths.CaptureFile, cfg.VideoSize);
                SetDlss5State(p.Dlss5);
                SetScalerChoice(p.Scaler, p.ScalerPick);
                fgWanted = p.Fg;
                SaveText(Paths.FgFile, p.Fg ? "1" : "0");
                // (1.3.1, SWITCH2: y su tipo de generacion, como LeaveHome: si no, seguia el del juego de la consola de antes hasta el primer juego)
                SetPicks(p.Pick60, p.Pick30);
            }
            AjustesSonDe(c);   // (1.3.1, SWITCH2)
            Log.Write("Consola " + c.Corto + (c.MenuPropio ? " (su menu a 4K nativo por su hora; cada juego con su perfil por su nombre en el centro de control / conmutador si " + (JuegoConsola.Activo(c, cfg) ? "esta encendido" : "se enciende " + (c.Familia == "xbox" ? "xbox" : "ps5") + "_reconocer_juegos") + ")" : " (su menu no se reconoce: sin HOME a 4K ni juegos por su icono)") +
                      ": los ajustes de toda la consola de partida, \"" +
                      p.Name + "\"" + (nuevo ? (deOtra != null ? " (nuevo, " + deOtra + ")" : " (nuevo, con los de ahora)") : "") + ": " + p.Summary());
        }

        // consola=auto: ha cambiado en marcha (lo dice Consolas.Pista desde el hilo que sea; al log ya lo ha escrito). Sin aviso en
        // pantalla: el usuario quiere "consola" a secas, sin el nombre de la detectada
        void OnConsolaCambiada(ConsolaPerfil antes, ConsolaPerfil nueva, string why)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { CambioDeConsola(nueva); }
                catch (Exception ex) { Log.Error("Cambio de consola", ex); }
            });
        }

        void CambioDeConsola(ConsolaPerfil nueva)
        {
            EsperasApagadoAlMotor(nueva);
            // 20-11 (CE-PERFILES): el menu propio (PS5, Xbox) siempre a 4K nativo (para el perfil por juego), aunque el ini traiga
            // home_4k_nativo=0; este solo manda sobre el HOME de la Switch 2 (ver ConsolaAlAbrir)
            cfg.HomeNativeEnabled = (homeIni && nueva.TieneHome) || nueva.MenuPropio;
            // Con HOME (la Switch 2): nada mas; en cuanto se vea su HOME, a 4K nativo como siempre (AnalyzeGrab / HomeTick)
            // 23-11 (SWITCH2): el juego de antes era de la otra consola ("Consola", "PS5: ..."): ya no es el que esta en marcha. 18:10:27,
            // recien cambiada a la Switch 2 desde la PS5, la vuelta del HOME a Ocarina of Time (en uso) se tomo por la vuelta a "Consola"
            // (sus ajustes, y lo cambiado con el mando se guardaba ahi). Sin el, se entra en el del cursor (el que esta en uso)
            if (nueva.TieneHome)
            {
                lock (profileSync)
                {
                    string cg = curGame;
                    if (cg != null && (cg == ConsolaPerfil.NombrePerfilConsola || JuegoConsola.TagConsola(cg) != null))
                    {
                        curGame = null;
                        curGameGuess = false;
                        Log.Write("Consola " + nueva.Corto + ": \"" + cg + "\" era de la consola de antes: ningun juego en marcha hasta ver su HOME");
                    }
                }
                return;
            }
            lock (homeSync) { ps5MenuHits = 0; ps5OutHits = 0; menuMaq.Reiniciar(); } // (19-11: con menu propio, su menu se mira desde ahora: MenuPs5Captura)
            // (19-11: ya a 4K nativo y la nueva con menu propio: se sigue asi, con los ajustes de la consola para cuando se salga del menu)
            if (nueva.MenuPropio && cfg.HomeNative && !homeGrace) { AplicarPerfilConsola(nueva); return; }
            // Sin HOME: el perfil de toda la consola. Desde el HOME a 4K (o el de paso) EnterGame lo hace con LeaveHome (telon y captura);
            // jugando, con los cambios del mando (SetLevel, RequestCapture...)
            lock (homeSync)
            {
                homeGame = null;
                labelNoName = false;
                noLabel = 0;
                enterSure = true;
                enterConfirmed = true;
                enterIcon = null;   // (23-11, SWITCH2: un icono del HOME de la consola de antes no es de esta)
            }
            EnterGame(nueva.PerfilConsola);
        }

        // 20-11 (CONSOLA): al cambiar de consola en marcha, su espera de "No Signal" y su negro con silencio al motor ya abierto (los aplica
        // al momento, sin reabrir la captura: ApplyPending). Antes se quedaban los de la consola de antes hasta reabrirlo (de la Switch 2 a
        // la PS5: 30 s de negro y silencio seguian cerrando la app). Con mpv + Magpie, la siguiente vez que se abra mpv
        void EsperasApagadoAlMotor(ConsolaPerfil nueva)
        {
            string set = "nosig_hold_ms=" + ((int)(EsperaSinSenalJugando(cfg) * 1000)).ToString(CultureInfo.InvariantCulture) + " black_secs=" + BlackSecsLua();
            if (!Config.EngineActive || !ipcUp) { Log.Write("Consola " + nueva.Corto + ": apagado " + set + " (al abrir otra vez la imagen)"); return; }
            lock (cmdQueue) cmdQueue.Add(JsonCmd("engine", "set " + set));
            Log.Write("Consola " + nueva.Corto + ": apagado al motor al momento (" + set + ")");
        }

        // RequestCapture, lo primero: una captura que no tiene sentido con esta consola (mas que lo que saca) no se pide
        bool CapturaSinSentido(string size)
        {
            ConsolaPerfil c = Consolas.Actual;
            if (c.CapturaUtil(size)) return false;
            if (!applying) Osd(c.AvisoCaptura(size, SizeName(size)));
            return true;
        }

        // "No Signal" jugando: lo del ini (apagado_espera_s) y, con una consola cuyo menu no se ve, como poco lo suyo (la PS5 y la Xbox
        // cambian de modo de video al entrar en un juego con HDR o 120 Hz). Tambien para el motor (nosig_hold_ms:
        // EngineArgs es estatico)
        // 20-11 (CONSOLA): con consola=auto y una consola sin probar (Xbox, PS4, Switch 1) u "otra" elegida sola, la espera mas larga de
        // todas (detras puede haber otra: el 20-11 la PS5 se tomo por la Xbox; Consolas.ApagadoPrudente)
        static double EsperaSinSenalJugando(Config cfg)
        {
            ConsolaPerfil c = Consolas.Actual;
            return Consolas.ApagadoPorMenuSeguro(c) ? cfg.NoSignalHoldSecs : Math.Max(cfg.NoSignalHoldSecs, Consolas.EsperaSinSenalMinS(c));
        }

        // black_secs del script de mpv: negro y silencio que se da por apagada. PlayStation: nunca (negro y silencio es el HDCP de una
        // app de video: lo avisa ObservaConsola); las demas, lo de su perfil. 20-11 (CONSOLA): con consola=auto y una sin probar u
        // "otra", nunca (con el perfil de la Xbox puesto por error delante de la PS5, 30 s de negro y silencio cerraban la app)
        static string BlackSecsLua()
        {
            return Consolas.NegroApagadoS(Consolas.Actual).ToString("0", CultureInfo.InvariantCulture);
        }

        // MpvLinkLoop / medidor de audio: el estado del audio de la consola (para el aviso de HDCP)
        void ConsolaAudio(string state) { if (state != null) audioNow = state; }

        // AnalyzeGrab, con una consola cuyo menu no se reconoce (lab = null, nada del HOME): con consola=auto, de reojo, el HOME de la
        // Switch 2 (sus franjas iguales a games\home_ref.txt en 2 capturas seguidas: la PS5 o la Xbox no las pueden dar) y las palabras
        // de la pantalla (OCR, 60-150 ms: cada 5 s los 3 primeros minutos, luego cada 30 s); y con una PlayStation, el aviso de HDCP
        void ObservaConsola(Bitmap full, Bitmap small, double[] strips, bool noSignal, double[] rf)
        {
            if (noSignal) { obsHomeHits = 0; darkSince = DateTime.MinValue; return; }
            DateTime now = DateTime.Now;
            if (!Consolas.Fija)
            {
                if (rf != null && HomeLabel.HomeDiff(strips, rf) <= 6)
                {
                    if (++obsHomeHits >= 2) { obsHomeHits = 0; Consolas.VistoHomeNintendo(true, 0); }
                }
                else obsHomeHits = 0;
                double every = (now - obsStart).TotalMinutes < 3 ? 5 : 30;
                // (23-11, SWITCH2 / XBOX: la imagen apunta a otra consola -una prueba propia suya hace menos de 20 s-: la pantalla cada
                // segundo, para confirmarla o descartarla enseguida; con la PS5 o la Xbox delante se lee "PS5", "Xbox Series"...)
                if (Consolas.ApuntaAOtra() != null) every = 1;
                if ((now - obsTextAt).TotalSeconds >= every)
                {
                    obsTextAt = now;
                    try { Consolas.VistoTexto(HomeLabel.ScreenText(full)); }
                    catch (Exception ex) { Log.Error("Consola: leyendo la pantalla", ex); }
                }
            }
            ConsolaPerfil c = Consolas.Actual;
            // 20-11 (HDCP120): el aviso de HDCP salia en falso CON imagen (fallo 2). Dos culpas conocidas: con captura 4K en yuy2 la copia
            // que da el motor puede venir solo en luma/gris (lit sale bajo aunque haya imagen) y el medidor de audio cambia sound/silent muy
            // seguido; con el umbral de antes (6 s, DarkLit = negro O carga) bastaba un rato oscuro para avisar. Ahora, conservador (mejor
            // no avisar que avisar en falso): negro DE VERDAD (lit ~0, mucho mas estricto que DarkLit, que cuenta tambien una carga oscura) +
            // en silencio, seguidos cfg.HdcpWarnSecs (de serie 20 s, antes 6). Un solo fotograma con algo de luz o con sonido reinicia la
            // cuenta: una copia 4K en luma/gris o un cambio suelto de sonido/silencio ya no disparan el aviso. Clave del ini hdcp_aviso=0 lo
            // apaga del todo. No afecta al apagado por consola (eso es NegroApagadoS, aparte: la PlayStation nunca se apaga por negro)
            if (!cfg.HdcpWarn || !c.HdcpEnJuegos) { darkSince = DateTime.MinValue; hdcpSaid = false; return; }
            double lit;
            HomeLabel.Thumb(small, out lit);
            bool dark = lit < HdcpDarkLit && audioNow == "silent";
            // 24-11 (1.3.3, VARIOS: "en gta 6 en una pantalla negra con algun hud quieto unos segundos se piensa que esta lo del hdcp"): el
            // HDCP da negro LISO en todo el cuadro; un HUD quieto, un logo o un texto sobre negro no (el lit 0,001 de su log era el HUD).
            // Ademas del umbral de la miniatura, la captura entera sin nada que se salga del fondo negro (src\Lanzador\NegroLiso.cs)
            NegroLiso.Medida liso = new NegroLiso.Medida();
            if (dark) { liso = NegroLiso.Mide(full); dark = liso.Liso; }
            if (!dark) { darkSince = DateTime.MinValue; hdcpSaid = false; return; }
            if (darkSince == DateTime.MinValue) { darkSince = now; return; }
            double secs = (now - darkSince).TotalSeconds;
            if (hdcpSaid || secs < cfg.HdcpWarnSecs) return;
            hdcpSaid = true;
            Log.Write("Consola " + c.Corto + ": negro liso y en silencio " + (int)secs + " s seguidos (lit " +
                      lit.ToString("0.###", CultureInfo.InvariantCulture) + ", " + liso + ", umbral " + cfg.HdcpWarnSecs.ToString("0.#", CultureInfo.InvariantCulture) +
                      " s; captura " + cfg.CaptureNow + "): puede ser el HDCP (" + c.ConsejoHdcp + ")");
            OnUi(delegate { OsdWarn(c.ConsejoHdcp, "8000"); });
        }

        // 23-11 (SWITCH2, el usuario: "he pasado de la ps5 a poner la switch 2 y no detecta que es la switch 2"): con otra consola puesta
        // (console.txt = ps5) solo contaban el HOME de la Switch 2 igual a games\home_ref.txt (que en esta version aun no estaba) y el texto
        // "Nintendo Switch 2" leido cada 5 s (2 pruebas con 5 s entre medias): se cambio a los 16 s, y mientras la PS5 caso la portada de
        // GTA VI con el HOME. Ahora, en CADA captura (antes que lo de la PS5): el nombre del juego en cian (HomeLabel.Selected, ~1 ms a
        // 640x360) y, solo si esta, la hora del PC arriba a la derecha con el wifi y la bateria (DetectorMenu.RelojSwitch, OCR de esa
        // zona). Las dos en la misma captura = el HOME de la Switch 2, prueba concluyente: a la segunda captura, Switch 2
        DateTime obsSwLog = DateTime.MinValue, obsSwRelojAt = DateTime.MinValue;
        void ObservaHomeSwitch(Bitmap full, Bitmap small, bool noSignal)
        {
            // (con la Switch 2 puesta, solo hasta que la imagen la confirme: la de console.txt al abrir)
            if (noSignal || Consolas.Fija || full == null || small == null) return;
            if (Consolas.Actual.TieneHome && Consolas.ConfirmadaPorImagen) return;
            try
            {
                Rectangle box;
                bool hint;
                HomeLabel.LabelText label;
                int hy;
                byte[] lab = HomeLabel.Selected(small, out box, out hint, out label, out hy);
                DateTime now = DateTime.Now;
                // (sin el nombre cian, la hora sola: una vez por segundo como mucho; con el, en cada captura)
                if (lab == null && (now - obsSwRelojAt).TotalSeconds < 1 && now >= obsSwRelojAt) return;
                if (lab == null) obsSwRelojAt = now;
                string rh, ro, rp;
                if (!DetectorMenu.RelojSwitch(full, now, out rh, out ro, out rp)) return;
                if ((now - obsSwLog).TotalSeconds >= 30 || now < obsSwLog)
                {
                    obsSwLog = now;
                    Log.Write("Consola: el HOME de la Switch 2 a la vista con " + Consolas.Actual.Corto + " puesta (" + (lab != null ? "el nombre del juego en cian y " : "") +
                              "la hora \"" + rh + "\" arriba a la derecha con la bateria): prueba " + (lab != null ? "concluyente" : "propia"));
                }
                if (lab != null) Consolas.VistoHomeSwitch2(rh);
                else Consolas.VistoRelojSwitch(rh);
            }
            catch (Exception ex) { Log.Error("Consola: buscando el HOME de la Switch 2", ex); }
        }

        // 23-11 (SWITCH2, al reves: "la he apagado, he encendido ps5 ... ponia juego 1 y juego 2"): con la Switch 2 puesta (console.txt) y la
        // PS5 delante, su menu solo contaba cada 5 s y nunca si el lector del HOME veia algo azul (MenuPs5Pista con switchHome): 76 s. Ahora,
        // en CADA captura con otra consola puesta (no una PlayStation), la hora de la PS5 en su sitio exacto (su lector, el de MenuPs5Captura,
        // que no vuelve a leer una zona que no ha cambiado) o, si arriba parece haber algo, las pestañas de su inicio: prueba concluyente
        void ObservaMenuPs5(Bitmap full, bool noSignal)
        {
            if (noSignal || Consolas.Fija || full == null || Consolas.Actual.Familia == "playstation") return;
            try
            {
                DateTime now = DateTime.Now;
                string h;
                if (!MenuPs5Visto(full, now, out h)) return;
                if ((now - obsPs5Log).TotalSeconds >= 30 || now < obsPs5Log)
                {
                    obsPs5Log = now;
                    Log.Write("Consola: el menu de la PS5 a la vista con " + Consolas.Actual.Corto + " puesta (" + (h != "pestañas" ? "la hora \"" + h + "\" en su sitio" : "las pestañas de su inicio") +
                              "): prueba concluyente");
                }
                Consolas.VistoMenuPs5Concluyente(h);
            }
            catch (Exception ex) { Log.Error("Consola: buscando el menu de la PS5", ex); }
        }
        DateTime obsPs5Log = DateTime.MinValue;

        // ¿El menu de la PS5? (su hora en su sitio exacto, o las pestañas de su inicio si arriba parece haber algo). hora = la leida o "pestañas"
        internal static bool MenuPs5Visto(Bitmap full, DateTime now, out string hora)
        {
            string ocr, porque;
            bool fuerte, parecio;
            if (DetectorMenu.BuscaReloj(full, now, Consolas.Ps5, out hora, out ocr, out fuerte, out porque, out parecio)) return true;
            string leido;
            hora = null;
            if ((parecio || (porque != null && porque.StartsWith("algo a la derecha", StringComparison.Ordinal))) && JuegoConsola.PestanasInicio(full, out leido))
            { hora = "pestañas"; return true; }
            return false;
        }

        // ChooseLook (cada 0,5 s): un juego a 40 (la consola a 120 Hz, la captura a 60: a tirones), al log una vez y como pista
        void MiraCadencia(double f, DateTime now)
        {
            if (Consolas.Cadencia(f) != "40") { fps40Since = DateTime.MinValue; fps40Said = false; return; }
            if (fps40Since == DateTime.MinValue) { fps40Since = now; return; }
            double secs = (now - fps40Since).TotalSeconds;
            if (fps40Said || secs < 20) return;
            fps40Said = true;
            // (1.3.2, MODO40: con el modo 40 se ve bien -una de cada 3 a 120 Hz o la cadencia 2-1 repartida-; si no, se dice que existe)
            if (cfg.Mode40Now) Log.Write("Juego a 40 imagenes por segundo (" + f.ToString("0.0", CultureInfo.InvariantCulture) + ") en el modo 40, captura a " + cfg.FramerateNow);
            else Log.Write("Juego a 40 imagenes por segundo (" + f.ToString("0.0", CultureInfo.InvariantCulture) + ") en el modo " + cfg.ModoFps + ": " +
                           "se ve a tirones. Mejor el modo 40 de la app (Menu del mando o el menu de ajustes)");
            Consolas.VistaCadencia(f, secs);
        }

        // WaitForSignal: el texto pequeño del aviso "Enciende la consola" (con una PlayStation, a los 12 s, el HDCP)
        static string PistaEsperando(double secs)
        {
            ConsolaPerfil c = Consolas.Actual;
            return c.HdcpEnJuegos && secs >= 12 ? "Si ya está encendida, desactiva su HDCP · Esc cancela" : "Pulsa Esc para cancelar";
        }
    }
}
