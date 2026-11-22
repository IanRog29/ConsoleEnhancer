// TiemposCarga.cs - 22-11-2026 (1.2.2, MENU; el usuario: "Reduce los tiempos de cargas y de aplicar perfiles si puedes").
//
// El motor en espera al abrir la app (motor_en_espera_al_abrir_ms): el "puente" (el motor jugando encima mientras Magpie prepara 7-11 s,
// src\SinTelon.cs) necesita un motor en espera listo, y ese motor tarda ~6,6 s en estarlo (slInit 3 s + slSetD3DDevice 2,9 s de
// Streamline, engine.log del usuario). Antes se arrancaba en el HOME a 4K nativo con 1 s de HOME quieto tras la primera imagen de mpv
// (WatchLoop): log del usuario 22-11 04:07:25 (app abierta) -> 04:07:29.04 arrancado -> 04:07:35.68 listo; la entrada en el juego a las
// 04:07:32.4 no lo tuvo: 4,3 s en negro hasta el puente tardio. Ahora se arranca a la vez que mpv al abrir la app (en el HOME a 4K
// nativo, sin capturadora: no estorba a mpv) y esta listo ~3,7 s antes. -1 = como antes
using System;
using System.Globalization;
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
