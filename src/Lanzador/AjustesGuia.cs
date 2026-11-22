// AjustesGuia.cs - 22-11-2026 (1.2.2, MENU): el boton central del mando (Guia / Xbox / PS / HOME) abre SIEMPRE el menu de ajustes o
// dice por que no, y "Aplicar" mientras Magpie prepara la imagen la reabre ya con lo nuevo.
//
// El usuario: "A veces le doy al boton del home del mando de pc y no se abre el menu de config, arregla eso." En su log (1.2.1) todas
// las veces eran "Menu de ajustes: no se abre: sin imagen (aun preparandose)": justo tras entrar en un juego o aplicar ajustes, mientras
// Magpie prepara el escalado (7-11 s creando los modelos de NVIDIA) el motor "puente" ya ensena la imagen (src\SinTelon.cs) pero
// lsReady sigue a false y el menu no se abria (ni decia nada en pantalla). Ademas GamepadLoop solo veia la Guia si seguia pulsada en
// su lectura de cada ~50 ms (una pulsacion corta se perdia: ahora PadHub.GuidePresses, src\Lanzador\Mandos.cs).
//
// Ahora, al pulsar la Guia (o Ctrl+O, o la bandeja):
//   - Si  : se abre ya. Tambien sobre el puente, el telon en vivo o el negro mientras Magpie prepara (entrar en un juego, reabrir
//           Magpie, mpv reabierto): las filas son las del juego en el que se esta entrando (sus ajustes ya estan puestos).
//   - Espera: algo pasajero (la deteccion del menu de la consola aun decide: 6 s desde que se abrio la imagen, 4 s desde que se salio del
//           menu de la consola; mpv reabriendose por otra captura; cambio de motor): aviso "Ajustes · se abren en un momento" y se abre
//           solo en cuanto se pueda (hasta 25 s; si no, dice por que).
//   - No  : en el menu de la consola (los ajustes son de cada juego: el aviso de siempre) o con otra ventana delante (lo dice).
// Los avisos los pinta una ventana propia (capa con alfa, encima de todo, sin foco): los de mpv no se ven con el puente o el telon encima.
// "Aplicar" con Magpie preparando y sin cambio de captura ni del 120 (ni de motor): los ajustes se ponen ya y WaitForScaling (el hilo que
// espera a Magpie, con lsModeSync) mata al Magpie que preparaba lo de antes, escribe la config nueva y lo abre otra vez (el puente sigue
// encima); antes se esperaba a que acabara (hasta 10 s, y si no, no se aplicaba) y luego otra reapertura entera (otros 8-11 s).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ConsoleEnhancer.Deteccion;

namespace ConsoleEnhancer
{
    partial class TrayApp
    {
        enum AjPuede { Si, Espera, No }

        int ajGuiaVistas = int.MinValue;      // PadHub.GuidePresses ya atendidas (GamepadLoop)
        volatile bool homeSwitchToHome;       // StartHomeSwitch: hacia el menu de la consola (true) o hacia un juego (false)
        int ajEsperaMagpie;                   // WaitForScaling en marcha (Interlocked)
        int ajReinicio;                       // 0 nada; 1 pedido por "Aplicar" con Magpie preparando; 2 tomado por WaitForScaling
        DateTime ajPedidoEn = DateTime.MinValue;   // la Guia pulsada sin poder abrirlo aun: se abre en cuanto se pueda
        string ajPedidoDesde = "";
        string ajPedidoPorque = "";
        System.Windows.Forms.Timer ajColaTimer;
        AvisoAjustes ajAviso;
        static int ajBarraMirada;

        // ---------------- la Guia (GamepadLoop) ----------------
        // ¿Se ha pulsado la Guia desde la ultima vez? (cualquier pulsacion, tambien una que ya se ha soltado: Mandos.cs GuidePresses)
        bool AjGuiaNueva(PadSnapshot s)
        {
            int n = PadHub.Shared.GuidePresses;
            if (ajGuiaVistas == int.MinValue) { ajGuiaVistas = n; return (s.Buttons & PadBtn.Guide) != 0 && !ajGuiaAntes; }
            if (n == ajGuiaVistas) return false;
            ajGuiaVistas = n;
            return true;
        }

        // La Guia con el menu cerrado (desde GamepadLoop): abrir, o ponerlo en cola, o decir por que no (en el hilo de la interfaz)
        void AjGuiaPulsada(string desde)
        {
            AjMirarBarraDeJuegos();
            OnUi(delegate { AjustesPedir(desde); });
        }

        // ---------------- abrir: ya, en cola o no ----------------
        // (hilo de la interfaz) desde: "boton central", "Ctrl+O", "bandeja"
        void AjustesPedir(string desde)
        {
            if (ajustesAbierto || userExit) return;
            string porque, aviso;
            AjPuede r = AjustesPuede(out porque, out aviso);
            if (r == AjPuede.Si)
            {
                AjColaQuitar();
                AjAvisoFuera();
                AjustesAbrir(desde + (porque != null ? ", " + porque : ""));
                return;
            }
            if (r == AjPuede.Espera)
            {
                bool ya = ajPedidoEn != DateTime.MinValue;
                if (!ya) { ajPedidoEn = DateTime.Now; ajPedidoDesde = desde; }
                ajPedidoPorque = porque;
                Log.Write("Menu de ajustes (" + desde + "): se abre en cuanto se pueda (" + porque + ")" + (ya ? " (ya pedido)" : ""));
                AjAvisar(Tx.T("Ajustes · se abren en un momento", "Settings · opening in a moment") + (aviso != null ? " · " + aviso : ""), false);
                AjColaPoner();
                return;
            }
            AjColaQuitar();
            Log.Write("Menu de ajustes (" + desde + "): no se abre: " + porque);
            AjAvisoNo(aviso);
        }

        // El aviso de un "no": el del menu de la consola lo pinta mpv como siempre (ahi se ve su imagen a 4K tal cual; mientras se cambia,
        // nada); los demas, el propio
        const string AjAvisoHome = "\u0001home";
        void AjAvisoNo(string aviso)
        {
            if (aviso == null) { AjAvisoFuera(); return; }
            if (aviso == AjAvisoHome)
            {
                AjAvisoFuera();
                if (!homeBusy) Osd(MenuTexto + " · " + Tx.T("los ajustes se cambian dentro de cada juego", "settings are changed inside each game"));
                return;
            }
            AjAvisar(aviso, false);
        }

        // ¿Se puede abrir ahora? porque: para el log; aviso: lo que se dice en pantalla (null = nada; AjAvisoHome = el del menu de la consola).
        // Solo mira: no pinta nada
        AjPuede AjustesPuede(out string porque, out string aviso)
        {
            porque = null; aviso = null;
            if (userExit) { porque = "saliendo"; return AjPuede.No; }
            if (AjPrueba) return AjPuede.Si;
            bool entrando = homeBusy && !homeSwitchToHome;   // del menu de la consola a un juego: Magpie preparando (puente o telon)
            if ((homeBusy && homeSwitchToHome) || (!homeBusy && InHome))
            {
                porque = "en el menu de la consola";
                aviso = AjAvisoHome;
                return AjPuede.No;
            }
            Process p = mpv;
            bool vivo = false;
            try { vivo = p != null && !p.HasExited; } catch { }
            bool cambiando = !lsReady || passesBusy || entrando || relaunchSize != null || relaunchEngine;
            IntPtr fg = GetForegroundWindow();
            if (!EsVentanaDeLaApp(fg))
            {
                // (cambiando de imagen el primer plano puede estar un momento en ninguna: en cola; jugando, otra ventana delante: no)
                porque = "la imagen no esta delante (delante: " + FgOwner() + ")";
                if (cambiando && vivo) return AjPuede.Espera;
                aviso = Tx.T("Ajustes · la imagen de la consola no está delante", "Settings · the console picture is not in front");
                return AjPuede.No;
            }
            if (!vivo)
            {
                porque = "sin imagen (" + (p == null ? "sin proceso" : "proceso cerrado") + (relaunchSize != null ? ", cambiando la captura" : relaunchEngine ? ", cambiando de motor" : "") + ")";
                return AjPuede.Espera;
            }
            // 1.0.12 (el usuario: "segun se abre la app ya deja darle al menu del home y desde el menu de consola tambien, no debe dejar"): en
            // los primeros segundos de una imagen y justo al salir del menu de la consola la deteccion aun decide. 1.2.2: en cola, no se pierde
            double desdeImagen = (DateTime.Now - imageStart).TotalSeconds, desdeMenu = (DateTime.Now - salioMenuEn).TotalSeconds;
            if (desdeImagen < 6) { porque = "la imagen acaba de abrirse (" + desdeImagen.ToString("0.0", CultureInfo.InvariantCulture) + " s)"; return AjPuede.Espera; }
            if (desdeMenu < 4) { porque = "se acaba de salir del menu de la consola (" + desdeMenu.ToString("0.0", CultureInfo.InvariantCulture) + " s)"; return AjPuede.Espera; }
            if (!cambiando) return AjPuede.Si;
            // Cambiando: con Magpie preparando (entrar en un juego, reabrir Magpie, mpv reabierto con el puente) se abre encima de lo que se vea
            if (relaunchSize != null) { porque = "cambiando la captura"; return AjPuede.Espera; }
            if (relaunchEngine && !RelevoToMagpieActive()) { porque = "cambiando de motor"; return AjPuede.Espera; }
            if (entrando || passesBusy || RelevoToMagpieActive() || Thread.VolatileRead(ref ajEsperaMagpie) > 0)
            {
                porque = "sobre " + (RelevoToMagpieActive() ? "el puente" : curtain != null ? "el telon" : "la imagen") + " mientras Magpie prepara";
                return AjPuede.Si;
            }
            porque = "sin imagen (aun preparandose)";
            return AjPuede.Espera;
        }

        // ¿Puede seguir abierto? (AjustesTick, cada 0,1-0,5 s)
        bool AjustesSigue(out string porque)
        {
            porque = null;
            if (userExit) { porque = "saliendo"; return false; }
            if (AjPrueba) return true;
            if ((homeBusy && homeSwitchToHome) || (!homeBusy && InHome)) { porque = "en el menu de la consola"; return false; }
            Process p = mpv;
            bool vivo = false;
            try { vivo = p != null && !p.HasExited; } catch { }
            if (vivo || RelevoToMagpieActive()) { ajSinImagenDesde = DateTime.MinValue; return true; }
            // (sin mpv: si se esta reabriendo -otra captura, otro motor-, un rato; si no, fuera)
            if (relaunchSize == null && !relaunchEngine) { porque = "la imagen ya no esta"; return false; }
            if (ajSinImagenDesde == DateTime.MinValue) ajSinImagenDesde = DateTime.Now;
            if ((DateTime.Now - ajSinImagenDesde).TotalSeconds > 15) { porque = "la imagen no vuelve (15 s)"; return false; }
            return true;
        }
        DateTime ajSinImagenDesde = DateTime.MinValue;

        // La imagen esta cambiando (el menu se reafirma encima mas a menudo: el puente, el telon o Magpie al presentar salen encima)
        bool AjCambiando() { return !lsReady || passesBusy || homeBusy || relaunchSize != null || relaunchEngine || relevo != null; }

        // Al abrirlo con el puente encima: con dlss5_sin_telon_encima=2 el motor se reafirma encima de todo cada 15 ms y taparia el menu
        void AjSobreElPuente()
        {
            Relevo r = relevo;
            if (r == null || r.ToEngine || r.Cut || cfg.Dlss5SeamlessOnTop < 2) return;
            r.Send(JsonCmd("script-message", "switch2-encima", "1"));
            Log.Write("Menu de ajustes: abierto sobre el puente: el motor deja de reafirmarse encima (switch2-encima 1) para no taparlo");
        }

        // ---------------- la cola ----------------
        void AjColaPoner()
        {
            if (ajColaTimer == null)
            {
                ajColaTimer = new System.Windows.Forms.Timer();
                ajColaTimer.Interval = 100;
                ajColaTimer.Tick += delegate { AjColaTick(); };
            }
            ajColaTimer.Start();
        }

        void AjColaQuitar()
        {
            ajPedidoEn = DateTime.MinValue;
            if (ajColaTimer != null) ajColaTimer.Stop();
        }

        void AjColaTick()
        {
            if (ajPedidoEn == DateTime.MinValue || ajustesAbierto || userExit) { AjColaQuitar(); return; }
            long ms = (long)(DateTime.Now - ajPedidoEn).TotalMilliseconds;
            string porque, aviso;
            AjPuede r = AjustesPuede(out porque, out aviso);
            if (r == AjPuede.Si)
            {
                string desde = ajPedidoDesde;
                AjColaQuitar();
                AjAvisoFuera();
                AjustesAbrir(desde + ", pedido hace " + ms + " ms" + (porque != null ? ", " + porque : ""));
                return;
            }
            if (r == AjPuede.No)
            {
                AjColaQuitar();
                Log.Write("Menu de ajustes: el pedido de hace " + ms + " ms ya no se abre: " + porque);
                AjAvisoNo(aviso);
                return;
            }
            ajPedidoPorque = porque;
            if (ms > 25000)
            {
                AjColaQuitar();
                Log.Write("Menu de ajustes: no se ha podido abrir en 25 s (" + porque + ")");
                AjAvisar(Tx.T("Ajustes · no se han podido abrir: la imagen no está lista", "Settings · could not open: the picture is not ready"), true);
            }
        }

        // ---------------- "Aplicar" con Magpie preparando ----------------
        // ¿En caliente? (antes de poner nada) Con Magpie preparando la imagen de mpv, y sin otra captura ni pasar a / desde el 120 (eso
        // reabre la captura: lo de siempre, al acabar)
        bool AjEnCaliente(Dictionary<string, string> e)
        {
            try
            {
                if (userExit || Config.EngineActive || relaunchSize != null || relaunchEngine || InHomeOHaciaEl()) return false;
                Process p = mpv;
                if (p == null || p.HasExited) return false;
                bool preparando = Thread.VolatileRead(ref ajEsperaMagpie) > 0 || passesBusy || (homeBusy && !homeSwitchToHome);
                if (!preparando || (lsReady && !passesBusy && !homeBusy)) return false;
                string v;
                if (e.TryGetValue("cap", out v) && v != cfg.VideoSize) return false;
                int modoAhora = cfg.Mode120 ? 120 : cfg.CapMode ? 30 : 60;
                if (e.TryGetValue("modo", out v) && v != modoAhora.ToString(CultureInfo.InvariantCulture) && (v == "120" || modoAhora == 120)) return false;
                return true;
            }
            catch { return false; }
        }

        bool InHomeOHaciaEl() { return (homeBusy && homeSwitchToHome) || (!homeBusy && InHome); }

        // (con los ajustes ya puestos) Que el hilo que espera a Magpie lo reabra con lo nuevo. false = no se puede (otro motor, ya ha acabado
        // de preparar...): lo de siempre
        bool AjReiniciarEnCaliente()
        {
            PrepareCfg();
            if (Config.EngineActive || cfg.EngineNow || cfg.HomeNative || !MagpieConfig.Needed(cfg))
            {
                Log.Write("Menu de ajustes: en caliente no (" + (cfg.EngineNow || Config.EngineActive ? "toca el motor propio" : "sin Magpie") + "): al acabar de preparar, lo de siempre");
                return false;
            }
            Interlocked.Exchange(ref ajReinicio, 1);
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 4000 && !userExit)
            {
                if (Thread.VolatileRead(ref ajReinicio) == 2) { Interlocked.Exchange(ref ajReinicio, 0); return true; }
                // (ya no se prepara nada: lo hara RestartLs, que solo reabre si la config de Magpie cambia)
                if (Thread.VolatileRead(ref ajEsperaMagpie) == 0 && lsReady && !passesBusy && !homeBusy) break;
                Thread.Sleep(10);
            }
            if (Interlocked.CompareExchange(ref ajReinicio, 0, 1) == 1)
            {
                Log.Write("Menu de ajustes: Magpie ya ha acabado de preparar (" + sw.ElapsedMilliseconds + " ms): se aplica como siempre");
                return false;
            }
            Interlocked.Exchange(ref ajReinicio, 0);
            return true;
        }

        // Desde WaitForScaling (su hilo, con lsModeSync), cada vuelta: si "Aplicar" lo ha pedido, Magpie fuera y otra vez con lo nuevo (el
        // puente o el telon siguen a la vista). true = reabierto: la espera vuelve a empezar
        bool AjReinicioEnEspera(Process p)
        {
            if (Interlocked.CompareExchange(ref ajReinicio, 2, 1) != 1) return false;
            try
            {
                if (p.HasExited || userExit) return false;
                if (LsHardChanges() == 0) { Log.Write("Menu de ajustes: Magpie ya se abrio con lo nuevo: sigue preparando"); return false; }
                Stopwatch ks = Stopwatch.StartNew();
                Log.Write("Menu de ajustes: aplicado con Magpie preparando: se reabre ya con lo nuevo (" +
                          (RelevoToMagpieActive() ? "el puente sigue encima" : curtain != null ? "con el telon" : "sin telon") + ")");
                KillMagpieNow("ajustes aplicados mientras preparaba", false);
                RestoreWindowSize(p);
                WriteMagpieConfigNow();
                WaitMagpieGone(ks);
                EnsureLossless();
                if (cfg.PerformanceBoost) ThreadPool.QueueUserWorkItem(delegate { Perf.BoostByName(MagpieLive.ProcName); });
                return true;
            }
            catch (Exception ex) { Log.Error("Menu de ajustes: reabriendo Magpie con lo nuevo", ex); return false; }
        }

        // WaitForScaling, contado (AjustesPuede / AjEnCaliente saben que hay un Magpie preparando)
        bool WaitForScaling(Process p, long logStart, int seconds = 15)
        {
            Interlocked.Increment(ref ajEsperaMagpie);
            try { return WaitForScalingCore(p, logStart, seconds); }
            finally { Interlocked.Decrement(ref ajEsperaMagpie); }
        }

        // Antes de aplicar lo de siempre: con algo cambiando aun (otra reapertura, Magpie preparando...), se espera a que acabe (hasta 45 s:
        // Magpie tarda 8-15 s; antes 10 s y despues no se aplicaba nada). false = no se aplica (el menu de la consola, la imagen se ha ido)
        bool AjEsperarParaAplicar()
        {
            Stopwatch sw = Stopwatch.StartNew();
            bool dicho = false;
            while (!userExit && sw.ElapsedMilliseconds < 45000 && (passesBusy || relaunchSize != null || !lsReady || relaunchEngine || homeBusy))
            {
                if (!dicho && sw.ElapsedMilliseconds > 400)
                {
                    dicho = true;
                    Log.Write("Menu de ajustes: la imagen esta cambiando: se aplica en cuanto acabe");
                    OnUi(delegate { AjAvisar(Tx.T("Ajustes · se aplican en cuanto la imagen esté lista", "Settings · applied as soon as the picture is ready"), false); });
                }
                Thread.Sleep(100);
            }
            if (userExit || InHome || homeBusy || !ImagenLista())
            {
                Log.Write("Menu de ajustes: no se aplica (" + (InHome ? "en el menu de la consola" : homeBusy ? "cambiando al menu de la consola" : "la imagen no esta lista en " + sw.ElapsedMilliseconds + " ms") + ")");
                if (!userExit) OnUi(delegate { AjAvisar(Tx.T("Ajustes · no aplicados: la imagen ha cambiado", "Settings · not applied: the picture changed"), true); });
                return false;
            }
            if (dicho) Log.Write("Menu de ajustes: imagen lista tras " + sw.ElapsedMilliseconds + " ms: se aplica");
            return true;
        }

        // ---------------- pruebas sin mando (switch2-test-guia / switch2-test-ajustes por la tuberia de mpv, con test_source) ----------------
        // switch2-test-guia = una pulsacion de la Guia; switch2-test-ajustes "nivel=3 pasadas=2 tipo=xess esc=dlss:K modo=60 fg=1 cap=..." =
        // "Aplicar" en el menu con eso cambiado. true = era una de estas
        bool AjPruebaEvento(string line)
        {
            if (cfg.TestSource.Length == 0 && !AjPrueba) return false;
            if (line.Contains("\"args\":[\"switch2-test-guia\""))
            {
                Log.Write("Prueba: Guia (switch2-test-guia)");
                if (ajustesAbierto) OnUi(delegate { AjustesCerrar("boton central"); });
                else AjGuiaPulsada("boton central (prueba)");
                return true;
            }
            if (line.Contains("\"args\":[\"switch2-test-ajustes\""))
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "switch2-test-ajustes\",\"([^\"]*)\"");
                Dictionary<string, string> e = new Dictionary<string, string>();
                if (m.Success)
                    foreach (string kv in m.Groups[1].Value.Split(' '))
                    {
                        int i = kv.IndexOf('=');
                        if (i > 0) e[kv.Substring(0, i)] = kv.Substring(i + 1);
                    }
                Log.Write("Prueba: Aplicar en el menu de ajustes (switch2-test-ajustes): " + (m.Success ? m.Groups[1].Value : "(nada)"));
                if (e.Count > 0)
                {
                    if (ajustesAbierto) OnUi(delegate { AjustesCerrar(null); });
                    ThreadPool.QueueUserWorkItem(delegate { try { AjustesPonerTodo(e); } catch (Exception ex) { Log.Error("Prueba: aplicando", ex); } });
                }
                return true;
            }
            return false;
        }

        // ---------------- la Barra de juegos de Xbox ----------------
        // Con "Abrir la Game Bar con este boton del mando" el boton Xbox la abre a la vez que el menu (y lo tapa). Se apunta una vez
        static void AjMirarBarraDeJuegos()
        {
            if (Interlocked.Exchange(ref ajBarraMirada, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    object v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", null);
                    if (v is int && (int)v != 0)
                        Log.Write("Aviso: la Barra de juegos de Xbox se abre con el boton Xbox del mando (Configuracion > Juegos > Game Bar): puede tapar el menu de ajustes y quitarle el primer plano a la imagen");
                }
                catch { }
            });
        }

        // ---------------- el aviso en pantalla (propio: se ve tambien sobre el puente y el telon) ----------------
        // (hilo de la interfaz) mal: punto rojo
        void AjAvisar(string texto, bool mal)
        {
            try
            {
                AjAvisoFuera();
                IntPtr img = mpvWindow;
                ajAviso = new AvisoAjustes(img, texto, mal);
                ajAviso.Empezar();
            }
            catch (Exception ex) { Log.Error("Menu de ajustes: aviso", ex); }
        }

        void AjAvisoFuera()
        {
            AvisoAjustes a = ajAviso;
            ajAviso = null;
            if (a != null) try { a.Dispose(); } catch { }
        }
    }

    // Una pastilla arriba en el centro, como los avisos de mpv y del motor: capa con alfa por pixel, siempre encima, sin activarse ni
    // coger el foco (la generacion de NVIDIA se apaga sin el), 2,6 s con fundidos. Se reafirma encima cada 100 ms (el puente, el telon y
    // la ventana de Magpie al presentar salen encima)
    sealed class AvisoAjustes : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Pt { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct Sz { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte Op, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)] struct BmpInfo { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPpm, YPpm, ClrUsed, ClrImportant; }
        [StructLayout(LayoutKind.Sequential)] struct Rc { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MonInfo { public int Size; public Rc Monitor, Work; public int Flags; }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BmpInfo bi, uint usage, out IntPtr bits, IntPtr sec, uint off);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool GdiFlush();
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref Pt pos, ref Sz size, IntPtr src, ref Pt srcPos, int key, ref Blend b, int flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Pt p, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MonInfo mi);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

        readonly int w, h;
        readonly Pt at;
        IntPtr memDc, dib, oldBmp;
        Bitmap surface;
        bool shown;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Stopwatch since = new Stopwatch();
        const int EntraMs = 140, DuraMs = 2600, SaleMs = 260;

        public AvisoAjustes(IntPtr imagen, string texto, bool mal)
        {
            IntPtr prevCtx = IntPtr.Zero;
            try { prevCtx = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { }   // PER_MONITOR_AWARE_V2: pixeles reales
            try
            {
                MonInfo mi = new MonInfo(); mi.Size = Marshal.SizeOf(typeof(MonInfo));
                IntPtr mon = imagen != IntPtr.Zero ? MonitorFromWindow(imagen, 2) : MonitorFromPoint(new Pt(), 1);
                Rectangle scr = GetMonitorInfo(mon, ref mi) ? Rectangle.FromLTRB(mi.Monitor.L, mi.Monitor.T, mi.Monitor.R, mi.Monitor.B) : Screen.PrimaryScreen.Bounds;
                float u = Math.Max(0.6f, scr.Height / 1080f);
                string tit = texto, det = "";
                int k = texto.IndexOf(" · ", StringComparison.Ordinal);
                if (k > 0) { tit = texto.Substring(0, k); det = texto.Substring(k); }
                using (Font fT = PrismaUi.TitleFont(22 * u), fD = PrismaUi.BodyFont(22 * u))
                using (Bitmap mb = new Bitmap(4, 4))
                using (Graphics mg = Graphics.FromImage(mb))
                {
                    float tw = mg.MeasureString(tit, fT).Width, dw = det.Length > 0 ? mg.MeasureString(det, fD).Width : 0;
                    w = (int)Math.Min(scr.Width - 40 * u, tw + dw + 96 * u);
                    h = (int)(64 * u);
                    at = new Pt { X = scr.X + (scr.Width - w) / 2, Y = scr.Y + (int)(48 * u) };
                    BmpInfo bi = new BmpInfo { Size = Marshal.SizeOf(typeof(BmpInfo)), Width = w, Height = -h, Planes = 1, BitCount = 32 };
                    IntPtr bits, screen = GetDC(IntPtr.Zero);
                    try { memDc = CreateCompatibleDC(screen); dib = CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0); }
                    finally { ReleaseDC(IntPtr.Zero, screen); }
                    if (memDc == IntPtr.Zero || dib == IntPtr.Zero || bits == IntPtr.Zero) { Dispose(); throw new InvalidOperationException("sin superficie para el aviso"); }
                    oldBmp = SelectObject(memDc, dib);
                    surface = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
                    using (Graphics g = Graphics.FromImage(surface))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        using (SolidBrush clear = new SolidBrush(Color.FromArgb(0, 0, 0, 0))) g.FillRectangle(clear, 0, 0, w, h);
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        RectangleF r = new RectangleF(1, 1, w - 2, h - 2);
                        using (GraphicsPath pp = PrismaUi.RoundRect(r, r.Height / 2))
                        {
                            using (SolidBrush bg = new SolidBrush(Color.FromArgb(236, 16, 19, 26))) g.FillPath(bg, pp);
                            using (Pen bp = new Pen(Color.FromArgb(52, 255, 255, 255), Math.Max(1f, 1.2f * u))) g.DrawPath(bp, pp);
                        }
                        Color dot = mal ? PrismaUi.Warn : PrismaUi.Accent;
                        float dx = 28 * u, dd = 12 * u;
                        using (SolidBrush db = new SolidBrush(dot)) g.FillEllipse(db, dx, (h - dd) / 2f, dd, dd);
                        float x = dx + dd + 16 * u;
                        StringFormat izq = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                        using (SolidBrush tb = new SolidBrush(PrismaUi.TitleColor)) g.DrawString(tit, fT, tb, new RectangleF(x, 0, w - x - 20 * u, h), izq);
                        if (det.Length > 0)
                            using (SolidBrush db2 = new SolidBrush(PrismaUi.DetailColor)) g.DrawString(det, fD, db2, new RectangleF(x + tw, 0, Math.Max(10, w - x - tw - 20 * u), h), izq);
                        izq.Dispose();
                    }
                }
                CreateParams cp = new CreateParams();
                cp.Caption = "ConsoleEnhancer · Aviso";
                cp.X = at.X; cp.Y = at.Y; cp.Width = w; cp.Height = h;
                cp.Style = unchecked((int)0x80000000);                  // WS_POPUP
                cp.ExStyle = 0x80000 | 0x20 | 0x08000000 | 0x80 | 0x8;   // LAYERED | TRANSPARENT | NOACTIVATE | TOOLWINDOW | TOPMOST
                CreateHandle(cp);
            }
            finally { try { if (prevCtx != IntPtr.Zero) SetThreadDpiAwarenessContext(prevCtx); } catch { } }
        }

        public void Empezar()
        {
            since.Start();
            timer.Interval = 33;
            timer.Tick += delegate { Paso(); };
            Mostrar(0);
            timer.Start();
        }

        void Paso()
        {
            long t = since.ElapsedMilliseconds;
            if (t >= EntraMs + DuraMs + SaleMs) { Dispose(); return; }
            double a = t < EntraMs ? t / (double)EntraMs : t > EntraMs + DuraMs ? 1 - (t - EntraMs - DuraMs) / (double)SaleMs : 1;
            Mostrar((byte)Math.Max(0, Math.Min(255, Math.Round(255 * a))));
            if (t % 100 < 34) Encima();
        }

        void Mostrar(byte a)
        {
            if (Handle == IntPtr.Zero || surface == null) return;
            GdiFlush();
            Pt dst = at, src = new Pt();
            Sz size = new Sz { W = w, H = h };
            Blend b = new Blend { Op = 0, Flags = 0, Alpha = a, Format = 1 };
            IntPtr screen = GetDC(IntPtr.Zero);
            try { UpdateLayeredWindow(Handle, screen, ref dst, ref size, memDc, ref src, 0, ref b, 2); }
            finally { ReleaseDC(IntPtr.Zero, screen); }
            if (!shown) { ShowWindow(Handle, 4); shown = true; Encima(); }   // SW_SHOWNOACTIVATE
        }

        void Encima() { if (Handle != IntPtr.Zero) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE: MA_NOACTIVATE
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try { timer.Stop(); timer.Dispose(); } catch { }
            if (Handle != IntPtr.Zero) DestroyHandle();
            if (surface != null) { surface.Dispose(); surface = null; }
            if (memDc != IntPtr.Zero) { if (oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp); DeleteDC(memDc); memDc = IntPtr.Zero; }
            if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        }
    }
}
