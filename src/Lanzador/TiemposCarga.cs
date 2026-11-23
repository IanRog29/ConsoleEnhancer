// TiemposCarga.cs - 22-11-2026 (1.2.2, MENU; el usuario: "Reduce los tiempos de cargas y de aplicar perfiles si puedes").
//
// El motor en espera al abrir la app (motor_en_espera_al_abrir_ms): el "puente" (el motor jugando encima mientras Magpie prepara 7-11 s,
// src\SinTelon.cs) necesita un motor en espera listo, y ese motor tarda ~6,6 s en estarlo (slInit 3 s + slSetD3DDevice 2,9 s de
// Streamline, engine.log del usuario). Antes se arrancaba en el HOME a 4K nativo con 1 s de HOME quieto tras la primera imagen de mpv
// (WatchLoop): log del usuario 22-11 04:07:25 (app abierta) -> 04:07:29.04 arrancado -> 04:07:35.68 listo; la entrada en el juego a las
// 04:07:32.4 no lo tuvo: 4,3 s en negro hasta el puente tardio. Ahora se arranca a la vez que mpv al abrir la app (en el HOME a 4K
// nativo, sin capturadora: no estorba a mpv) y esta listo ~3,7 s antes. -1 = como antes
//
// nvngx_dlss.dll de Magpie solo si su cadena lleva DLSS SR (magpie_dlss_sr_solo_cadena): NGX, al empezar cada escalado de Magpie, carga
// TODOS los "snippets" que encuentra en su ruta de busqueda (la carpeta magpie\) y prepara sus kernels, se usen o no: con la captura a 4K
// ("sin escalar": DLSSNR y la generacion, sin DLSS SR) el de DLSS SR (310.7.0) se llevaba ~1,5-2,5 s (magpie.log del usuario: buscarlo,
// abrirlo, sus cubins). El driver no trae otro DLSS SR (solo DLSS-G en DriverStore), asi que sin el fichero NGX no lo carga. Antes de
// abrir Magpie (EnsureLossless, PrestartMagpie) se aparta (nvngx_dlss.dll.ce-sin-uso) o se devuelve segun la cadena que se ha escrito;
// al abrir la app se devuelve siempre (el manifiesto de Magpie lo pide). El motor propio usa el suyo (engine\), no este.
// Medido el 22-11 (fuente de prueba a 4K, DLSS 5 nivel 2-3 con 1 pasada + DLSS FG o XeSS FG; la tele en reposo, a 60 Hz): de empezar a
// escalar a su primera imagen 7,3-7,7 s con el DLL y 5,5-6,3 s sin el (el nucleo de NGX 4,6-5,1 s -> 3,1-3,7 s); DLSSNR igual
// (created=true, 0 errores). Con DLSS SR en la cadena (1080p) se devuelve y va como siempre. Aplicar / entrar en un juego a 4K: ~1,5-2,4 s
// menos. El motor en espera al abrir la app: listo a los 7,1-7,8 s (antes 7,8-8,3 s en la prueba; en el log del usuario 10,4 s)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace ConsoleEnhancer
{
    partial class Config
    {
        public int StandbyOpenMs = 0;   // motor_en_espera_al_abrir_ms: ms tras lanzar mpv al abrir la app (0 = a la vez; -1 = como antes)

        public bool ParseTiempos(string key, string val)
        {
            switch (key)
            {
                case "motor_en_espera_al_abrir_ms":
                    {
                        int v;
                        StandbyOpenMs = int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? Math.Max(-1, Math.Min(10000, v)) : 0;
                        return true;
                    }
                case "magpie_dlss_sr_solo_cadena": return true;   // (1.2.2: el nvngx_dlss.dll de Magpie; 1.2.4 ronda 6: sin Magpie, no hace nada)
            }
            return false;
        }
    }

    partial class TrayApp
    {
        // RunMpv, al lanzar el primer mpv de la sesion (el HOME a 4K nativo): el motor en espera ya, en otro hilo
        void StandbyAlAbrir()
        {
            int ms = cfg.StandbyOpenMs;
            if (ms < 0 || !cfg.HomeNative || Config.EngineActive) return;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    if (ms > 0) Thread.Sleep(ms);
                    if (userExit || !cfg.HomeNative || Config.EngineActive || standbyEngine != null || standbyLaunchBusy) return;
                    Log.Write("Motor en espera: se arranca ya al abrir la app, a la vez que mpv (motor_en_espera_al_abrir_ms=" + ms + "; antes en el HOME tras 1 s quieto)");
                    MaybePrestartStandby();
                }
                catch (Exception ex) { Log.Error("Motor en espera al abrir la app", ex); }
            });
            t.IsBackground = true;
            t.Name = "Motor en espera (al abrir)";
            t.Start();
        }
    }
}
