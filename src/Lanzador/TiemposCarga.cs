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
        public bool MagpieDlssSrOnlyChain = true;   // magpie_dlss_sr_solo_cadena: 0 = nvngx_dlss.dll siempre en magpie\ (como antes)

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
                case "magpie_dlss_sr_solo_cadena":
                    {
                        string v = val.Trim().ToLowerInvariant();
                        MagpieDlssSrOnlyChain = !(v == "0" || v == "no" || v == "false" || v == "off");
                        return true;
                    }
            }
            return false;
        }
    }

    // El nvngx_dlss.dll de la carpeta de Magpie, apartado o en su sitio
    static class DllDlssSr
    {
        public const string Nombre = "nvngx_dlss.dll", Aparte = "nvngx_dlss.dll.ce-sin-uso";
        static string estado = "";   // lo ultimo apuntado en el log

        // lleva: la cadena de Magpie lleva DLSS SR (o no se quiere apartar). Nunca con Magpie abierto (lo podria tener cargado)
        public static void Poner(bool lleva, string porque)
        {
            try
            {
                string a = Path.Combine(Paths.MagpieDir, Nombre), b = Path.Combine(Paths.MagpieDir, Aparte);
                if (lleva)
                {
                    if (File.Exists(a) || !File.Exists(b)) return;
                    File.Move(b, a);
                    Apuntar("Magpie: " + Nombre + " otra vez en su sitio (" + porque + ")");
                    return;
                }
                if (!File.Exists(a)) return;
                if (MagpieLive.Running) return;
                // (una carpeta de Magpie que es una union -bin\magpie -> terceros\magpie al compilar- es de todos: no se toca)
                if ((File.GetAttributes(Paths.MagpieDir) & FileAttributes.ReparsePoint) != 0) { Apuntar("Magpie: " + Nombre + " no se aparta (la carpeta de Magpie es una union a otra: compartida)"); return; }
                if (File.Exists(b)) File.Delete(b);
                File.Move(a, b);
                Apuntar("Magpie: " + Nombre + " apartado (" + porque + "): su cadena no lleva DLSS SR y NGX no lo carga al preparar (~1-2 s menos)");
            }
            catch (Exception ex) { Apuntar("Aviso: Magpie: " + Nombre + " sin " + (lleva ? "devolver" : "apartar") + " (" + ex.Message + ")"); }
        }

        static void Apuntar(string s) { if (s == estado) return; estado = s; Log.Write(s); }
    }

    partial class TrayApp
    {
        // Antes de abrir Magpie, con su config ya escrita (cfg con lo de ahora): DLSS SR en la cadena -> nvngx_dlss.dll en su sitio; sin el, aparte
        void MagpieDlssSrSegunCadena()
        {
            bool lleva = true;
            try
            {
                if (cfg.MagpieDlssSrOnlyChain)
                {
                    lleva = false;
                    foreach (object o in MagpieConfig.Chain(cfg, double.NaN))
                    {
                        Dictionary<string, object> d = o as Dictionary<string, object>;
                        object n;
                        if (d != null && d.TryGetValue("name", out n) && string.Equals(n as string, "DLSS\\DLSS_SR", StringComparison.OrdinalIgnoreCase)) { lleva = true; break; }
                    }
                }
            }
            catch (Exception ex) { lleva = true; Log.Error("Magpie: mirando si su cadena lleva DLSS SR", ex); }
            DllDlssSr.Poner(lleva, lleva ? (cfg.MagpieDlssSrOnlyChain ? "la cadena lleva DLSS SR" : "magpie_dlss_sr_solo_cadena=0") : "captura " + SizeName(cfg.VideoSize) + ", escalador " + cfg.ScalerNow);
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
